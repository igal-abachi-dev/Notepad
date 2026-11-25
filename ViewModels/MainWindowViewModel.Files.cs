using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;
using MsBox.Avalonia.Enums;
using Notepad.NeoEdit;
using NotepadAvalonia.Models;
using NotepadAvalonia.Services;
using NotepadAvalonia.Views;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NotepadAvalonia.ViewModels;

public partial class MainWindowViewModel
{
    private static readonly SemaphoreSlim SaveQueue = new(1);
    private static readonly FilePickerFileType TextFiles = new("Text Documents") { Patterns = ["*.txt"] };
    private static readonly IReadOnlyList<FilePickerFileType> FileTypes = [TextFiles, FilePickerFileTypes.All];
    private long _loadRequest;
    private bool _saveDecisionPending;
    private bool _savePickerPending;
    private int _pendingWrites;
    private TaskCompletionSource? _saveIdle;
    private string? _pendingLogAppend;
    internal bool HasPendingSave => _saveIdle != null || _savePickerPending;

    [RelayCommand]
    private async Task NewFileAsync()
    {
        if (!await CheckSaveChangesAsync()) return;
        var previous = Document;
        _loadRequest++;
        _pendingLogAppend = null;
        CurrentLanguage = SyntaxLanguage.Plain;
        Document = CreateUntitledDocument();
        ResetEditor("");
        UpdateEncodingDisplay();
        UpdateStatus();
        await ReleaseDocumentDraftAsync(previous, discard: true);
    }

    private DocumentModel CreateUntitledDocument()
    {
        var encoding = Settings.DefaultEncoding;
        return new DocumentModel { Encoding = _fileService.GetEncoding(encoding), EncodingType = encoding, SaveEncodingType = encoding };
    }

    [RelayCommand]
    private void NewWindow()
    {
        var viewModel = new MainWindowViewModel(_fileService, new SearchService(), _settingsService, _printService, _timeProvider, _draftStore);
        var window = new MainWindow { DataContext = viewModel };
        window.Show();
    }

    [RelayCommand]
    public Task OpenFileAsync(string? path = null) => OpenAsync(path, selectEncoding: false);

    [RelayCommand]
    private Task OpenWithEncodingAsync() => OpenAsync(null, selectEncoding: true);

    [RelayCommand]
    private Task ReopenWithEncodingAsync() =>
        Document.IsUntitled ? Task.CompletedTask : OpenAsync(Document.FilePath, selectEncoding: true);

    private async Task OpenAsync(string? path, bool selectEncoding)
    {
        var original = Document;
        long revision = _editVersion;
        try
        {
            if (string.IsNullOrEmpty(path))
            {
                var owner = GetMainWindow();
                var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Open",
                    FileTypeFilter = FileTypes,
                    SuggestedStartLocation = await GetDocumentFolderAsync(owner, original)
                });
                if (files.Count == 0) return;
                path = files[0].TryGetLocalPath() ?? throw new IOException("Select a file on an accessible file system.");
            }

            FileEncodingType? encoding = null;
            if (selectEncoding)
            {
                var picker = new EncodingDialog(null, forOpening: true);
                var choice = await picker.ShowDialog<EncodingDialog.EncodingOption?>(GetMainWindow());
                if (choice == null) return;
                encoding = choice.Type;
            }

            if (Document != original || _editVersion != revision || !await CheckSaveChangesAsync()) return;
            await LoadFileAsync(path, encoding);
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            await ShowErrorAsync($"Cannot open file. {ex.Message}");
        }
    }

    public async Task<bool> LoadFileAsync(string path, FileEncodingType? requestedEncoding = null)
    {
        var previousDocument = Document;
        long previousVersion = _editVersion;
        long request = ++_loadRequest;
        var windowsOnly = Settings.WindowsOnlyLineEndings;
        LoadedFile file;
        try
        {
            path = Path.GetFullPath(path);
            file = await _fileService.ReadDocumentAsync(path, requestedEncoding, windowsOnly);
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            if (!IsCurrent()) return false;
            var hint = ex is DecoderFallbackException ? " Use File > Open with Encoding to select another encoding." : "";
            await ShowErrorAsync($"Cannot open {Path.GetFileName(path)}. {ex.Message}{hint}");
            return false;
        }

        if (!IsCurrent()) return false;
        _pendingLogAppend = file.Content.StartsWith(".LOG", StringComparison.Ordinal)
            ? $"\r\n{FormatTimeDate(_timeProvider.GetLocalNow().DateTime)}\r\n" : null;
        Document = new DocumentModel
        {
            FilePath = path,
            IsUntitled = false,
            IsModified = _pendingLogAppend != null,
            Encoding = file.Encoding,
            EncodingType = file.EncodingType,
            SaveEncodingType = file.EncodingType,
            LineEnding = file.LineEnding,
            SavedFileVersion = file.Version
        };
        CurrentLanguage = SyntaxMatcher.DetectLanguage(path);
        ResetEditor(file.Content);
        UpdateEncodingDisplay();
        UpdateStatus();
        AddRecentFile(path);
        PersistSettings();
        await ReleaseDocumentDraftAsync(previousDocument, discard: true);
        return true;

        bool IsCurrent() => Document == previousDocument && _editVersion == previousVersion && _loadRequest == request;
    }

    private static string FormatTimeDate(DateTime moment) =>
        $"{moment.ToString("t", CultureInfo.CurrentCulture)} {moment.ToString("d", CultureInfo.CurrentCulture)}";

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SaveFileAsync() => await SaveCurrentDocumentAsync();

    private Task<bool> SaveCurrentDocumentAsync() =>
        Document.IsUntitled
            ? PickSaveDestinationAsync(Document)
            : SaveWithRetryAsync(Document, Document.FilePath, Document.SaveEncodingType);

    [RelayCommand]
    private async Task SaveFileAsAsync() => await PickSaveDestinationAsync(Document);

    private async Task<bool> PickSaveDestinationAsync(DocumentModel original)
    {
        if (_savePickerPending) return false;
        _savePickerPending = true;
        try
        {
            var owner = GetMainWindow();
            var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save As",
                DefaultExtension = "txt",
                SuggestedFileName = original.IsUntitled ? null : original.FileName,
                SuggestedStartLocation = await GetDocumentFolderAsync(owner, original),
                FileTypeChoices = FileTypes,
                ShowOverwritePrompt = true
            });
            if (file == null || Document != original) return false;
            var path = file.TryGetLocalPath() ?? throw new IOException("Select a file on an accessible file system.");
            var picker = new EncodingDialog(original.SaveEncodingType);
            var choice = await picker.ShowDialog<EncodingDialog.EncodingOption?>(owner);
            return choice?.Type is { } encoding && Document == original && await SaveWithRetryAsync(original, path, encoding);
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            await ShowErrorAsync($"Cannot save file. {ex.Message}");
            return false;
        }
        finally
        {
            _savePickerPending = false;
        }
    }

    private static async Task<IStorageFolder?> GetDocumentFolderAsync(Window owner, DocumentModel original) =>
        original.IsUntitled || Path.GetDirectoryName(original.FilePath) is not { } folder
            ? null
            : await owner.StorageProvider.TryGetFolderFromPathAsync(folder);

    private async Task<bool> SaveWithRetryAsync(DocumentModel original, string path, FileEncodingType encoding)
    {
        try
        {
            return await SaveRevisionAsync(original, path, encoding);
        }
        catch (EncoderFallbackException) when (encoding == FileEncodingType.ANSI && Document == original)
        {
            var answer = await ShowMessageAsync("Save encoding",
                "Some characters cannot be saved in ANSI. Save this file as UTF-8?", ButtonEnum.YesNo);
            if (answer != ButtonResult.Yes || Document != original) return false;
            try
            {
                return await SaveRevisionAsync(original, path, FileEncodingType.UTF8);
            }
            catch (Exception ex) when (IsFileError(ex))
            {
                await ShowErrorAsync($"Cannot save file. {ex.Message}");
                return false;
            }
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            await ShowErrorAsync($"Cannot save file. {ex.Message}");
            return false;
        }
    }

    private async Task<bool> SaveRevisionAsync(DocumentModel original, string path, FileEncodingType encoding)
    {
        if (Document != original || EditorControl is not { } editor) return false;
        long revision = editor.CaptureSaveRevision();
        string content = editor.GetText();
        var lineEnding = original.LineEnding;
        path = Path.GetFullPath(path);

        _pendingWrites++;
        _saveIdle ??= new(TaskCreationOptions.RunContinuationsAsynchronously);
        await SaveQueue.WaitAsync();
        try
        {
            var expected = !original.IsUntitled && SettingsService.PathComparer.Equals(original.FilePath, path)
                ? original.SavedFileVersion : null;
            var fileVersion = await _fileService.CommitFileAsync(path, content, encoding, lineEnding, expected);
            if (Document != original || EditorControl != editor) return true;
            editor.MarkSaved(revision);
            original.FilePath = path;
            original.IsUntitled = false;
            original.IsModified = editor.IsModified;
            original.EncodingType = original.SaveEncodingType = encoding;
            original.Encoding = _fileService.GetEncoding(encoding);
            original.SavedFileVersion = fileVersion;
            CurrentLanguage = SyntaxMatcher.DetectLanguage(path);
            OnPropertyChanged(nameof(Document));
            UpdateEncodingDisplay();
            AddRecentFile(path);
            PersistSettings();
            if (original.IsModified)
            {
                original.DraftVersion = -1;
                ScheduleDraftCheckpoint();
            }
            else
                await ReleaseDocumentDraftAsync(original, discard: true);
            return true;
        }
        finally
        {
            SaveQueue.Release();
            if (--_pendingWrites == 0)
            {
                var idle = _saveIdle;
                _saveIdle = null;
                idle!.SetResult();
            }
        }
    }

    public async Task<bool> CheckSaveChangesAsync(bool discardDraft = false)
    {
        if (_saveDecisionPending || _savePickerPending) return false;
        _saveDecisionPending = true;
        var original = Document;
        try
        {
            while (_saveIdle is { } idle)
                await idle.Task;
            if (Document != original) return false;
            if (!original.IsModified) return true;
            if (OwnerWindow is not { IsVisible: true } owner) return false;
            var result = await SaveChangesDialog.AskAsync(owner, original.FileName);
            if (Document != original) return false;
            if (result == ButtonResult.Yes) return await SaveCurrentDocumentAsync() && Document == original && !original.IsModified;
            if (result != ButtonResult.No) return false;
            if (discardDraft) await ReleaseDocumentDraftAsync(original, discard: true);
            return true;
        }
        finally
        {
            _saveDecisionPending = false;
        }
    }

    [RelayCommand]
    public async Task OpenRecentFileAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        if (!File.Exists(path))
        {
            RecentFiles.Remove(path);
            PersistSettings();
            await ShowErrorAsync("File not found.");
            return;
        }
        await OpenFileAsync(path);
    }

    internal async Task OpenStartupFileAsync(string path, FileEncodingType? encoding, bool print)
    {
        if (!File.Exists(path) && !Path.HasExtension(path)) path += ".txt";
        if (!File.Exists(path))
        {
            var choice = await ShowMessageAsync("Notepad", $"Cannot find {path}. Do you want to create it?", ButtonEnum.YesNoCancel);
            if (choice == ButtonResult.Cancel) { OwnerWindow?.Close(); return; }
            if (choice != ButtonResult.Yes) return;
            try
            {
                using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            }
            catch (Exception ex) when (IsFileError(ex))
            {
                await ShowErrorAsync($"Cannot create file. {ex.Message}");
                return;
            }
        }

        if (await LoadFileAsync(path, encoding) && print)
        {
            if (await PrintDocumentAsync(direct: true)) OwnerWindow?.Close();
        }
    }

    private static bool IsFileError(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;
}
