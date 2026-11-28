using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;
using Notepad.NeoEdit;
using NotepadAvalonia.Models;
using NotepadAvalonia.Services;
using NotepadAvalonia.Views;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NotepadAvalonia.ViewModels;

public partial class MainWindowViewModel
{
    private static readonly SemaphoreSlim SaveQueue = new(1);
    private long _loadRequest;
    private bool _saveDecisionPending;
    private bool _savePickerPending;
    private bool _hardLinkPromptPending;
    private int _pendingWrites;
    private TaskCompletionSource? _saveIdle;
    private string? _pendingLogAppend;
    internal bool HasPendingSave => _saveIdle != null || _savePickerPending || _hardLinkPromptPending;

    [RelayCommand]
    private async Task NewFileAsync()
    {
        if (!await CheckSaveChangesAsync()) return;
        var previous = Document;
        _loadRequest++;
        _pendingLogAppend = null;
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
    private Task NewWindow() => LaunchNewProcessAsync();

    private async Task<bool> LaunchNewProcessAsync(params string[] arguments)
    {
        try
        {
            using var start = Process.Start(NotepadProcess.CreateStartInfo(Environment.ProcessPath, AppContext.BaseDirectory, arguments));
            return start != null;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or Win32Exception)
        {
            await ShowErrorAsync($"Cannot open a new Notepad window. {ex.Message}");
            return false;
        }
    }

    [RelayCommand]
    public Task OpenFileAsync(string? path = null) => OpenAsync(path, selectEncoding: false);

    [RelayCommand]
    private Task OpenWithEncodingAsync() => OpenAsync(null, selectEncoding: true);

    [RelayCommand]
    private Task ReopenWithEncodingAsync() =>
        Document.IsUntitled ? Task.CompletedTask : OpenAsync(Document.FilePath, selectEncoding: true);

    [RelayCommand]
    private async Task ReloadFromDiskAsync()
    {
        var active = Document;
        if (active.IsUntitled || !await CheckSaveChangesAsync() || active != Document) return;
        await LoadFileAsync(active.FilePath, active.EncodingType, appendLog: false);
    }

    [RelayCommand]
    private Task ViewFileAsEncodingAsync(FileEncodingType? encoding) =>
        Document.IsUntitled ? Task.CompletedTask : OpenAsync(Document.FilePath, selectEncoding: false, requestedEncoding: encoding);

    [RelayCommand]
    private async Task OpenContainingFolderAsync()
    {
        if (Document.IsUntitled || OwnerWindow is not { } window) return;
        try
        {
            string path = DocumentPath.GetFullPath(Document.FilePath);
            var folder = new DirectoryInfo(Path.GetDirectoryName(path)!);
            bool hasWindowsHandle = window.TryGetPlatformHandle()?.HandleDescriptor == "HWND";
            bool selected = await Task.Run(() =>
            {
                if (!folder.Exists) throw new DirectoryNotFoundException("The folder is unavailable.");
                return OperatingSystem.IsWindows() && hasWindowsHandle && File.Exists(path) && WindowsFileLocation.TrySelect(path);
            });
            if (!selected && !await window.Launcher.LaunchDirectoryInfoAsync(folder))
                throw new IOException("The file manager did not open the folder.");
        }
        catch (Exception ex) when (IsFileError(ex) || ex is Win32Exception or InvalidOperationException)
        {
            await ShowErrorAsync($"Cannot open the containing folder. {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task ChangeDefaultEncodingAsync()
    {
        var dialog = new EncodingDialog(Settings.DefaultEncoding);
        dialog.SetPrompt("Select the default encoding for new documents.");
        var choice = await dialog.ShowDialog<EncodingDialog.EncodingOption?>(GetMainWindow());
        if (choice?.Type is not { } encoding) return;
        Settings.DefaultEncoding = encoding;
        if (Document is { IsUntitled: true, IsModified: false } && EditorControl?.TextLength == 0)
        {
            Document.EncodingType = Document.SaveEncodingType = encoding;
            Document.Encoding = _fileService.GetEncoding(encoding);
            UpdateEncodingDisplay();
        }
        PersistSettings();
    }

    private async Task OpenAsync(string? path, bool selectEncoding, FileEncodingType? requestedEncoding = null)
    {
        var original = Document;
        long revision = _editVersion;
        try
        {
            FileEncodingType? encoding = requestedEncoding;
            if (string.IsNullOrEmpty(path))
            {
                var file = await PickFileAsync(original, saveEncoding: null);
                if (file == null) return;
                (path, encoding, _) = file;
                selectEncoding = false;
            }

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

    public async Task<bool> LoadFileAsync(string path, FileEncodingType? requestedEncoding = null, bool appendLog = true)
    {
        var previousDocument = Document;
        long previousVersion = _editVersion;
        long request = ++_loadRequest;
        var windowsOnly = Settings.WindowsOnlyLineEndings;
        LoadedFile? file;
        bool openedCopy = false;
        long approvedLength = 0;
        try
        {
            path = DocumentPath.GetFullPath(path);
            try
            {
                file = await _fileService.ReadDocumentForEditorAsync(path, requestedEncoding, windowsOnly, ConfirmLargeFileAsync);
            }
            catch (DecoderFallbackException) when (IsCurrent() && OwnerWindow is { IsVisible: true })
            {
                var choice = await ShowMessageAsync("Invalid text encoding",
                    $"{Path.GetFileName(path)} contains invalid bytes for this encoding. Open an untitled copy with replacement characters? " +
                    "Choose No to try a different encoding.", MessageButtons.YesNo);
                if (choice != DialogChoice.Yes || !IsCurrent()) return false;
                file = await _fileService.ReadDocumentForEditorAsync(path, requestedEncoding, windowsOnly, ConfirmLargeFileAsync,
                    replaceInvalidBytes: true);
                openedCopy = true;
            }
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            if (!IsCurrent()) return false;
            var hint = ex is DecoderFallbackException ? " Use File > Open with Encoding to select another encoding." : "";
            await ShowErrorAsync($"Cannot open {Path.GetFileName(path)}. {ex.Message}{hint}");
            return false;
        }

        if (file == null || !IsCurrent()) return false;
        _pendingLogAppend = appendLog && !openedCopy && file.Content.StartsWith(".LOG", StringComparison.Ordinal)
            ? $"\r\n{FormatTimeDate(_timeProvider.GetLocalNow().DateTime)}\r\n" : null;
        var encodingType = openedCopy ? FileEncodingType.UTF8 : file.EncodingType;
        Document = new DocumentModel
        {
            FilePath = openedCopy ? "" : path,
            IsUntitled = openedCopy,
            IsModified = openedCopy || _pendingLogAppend != null,
            IsReadOnlyFile = !openedCopy && file.IsReadOnly,
            Encoding = openedCopy ? _fileService.GetEncoding(encodingType) : file.Encoding,
            EncodingType = encodingType,
            SaveEncodingType = encodingType,
            LineEnding = file.LineEnding,
            SavedFileVersion = openedCopy ? null : file.Version
        };
        ResetEditor(file.Content);
        UpdateEncodingDisplay();
        UpdateStatus();
        if (!openedCopy) AddRecentFile(path);
        else ScheduleDraftCheckpoint();
        PersistSettings();
        await ReleaseDocumentDraftAsync(previousDocument, discard: true);
        return true;

        bool IsCurrent() => Document == previousDocument && _editVersion == previousVersion && _loadRequest == request;

        async Task<bool> ConfirmLargeFileAsync(long length)
        {
            if (!IsCurrent()) return false;
            if (length <= approvedLength) return true;
            if (ActiveDialogOwner is not { IsVisible: true } owner) return false;

            var choice = await MessageDialog.ShowAsync(owner, "Open large file",
                $"{Path.GetFileName(path)} is {length / (1024d * 1024):N1} MiB. " +
                "Opening it loads the entire file into memory and may take a while.",
                [("_Open", DialogChoice.Ok), ("Cancel", DialogChoice.Cancel)], defaultChoice: DialogChoice.Cancel);
            if (choice != DialogChoice.Ok || !IsCurrent()) return false;
            approvedLength = length;
            return true;
        }
    }

    private static string FormatTimeDate(DateTime moment) =>
        $"{moment.ToString("t", CultureInfo.CurrentCulture)} {moment.ToString("d", CultureInfo.CurrentCulture)}";

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SaveFileAsync()
    {
        if (!IsFollowingLog && !_hardLinkPromptPending && !_savePickerPending) await SaveCurrentDocumentAsync();
    }

    private Task<bool> SaveCurrentDocumentAsync() =>
        Document.IsUntitled
            ? PickSaveDestinationAsync(Document)
            : SaveWithRetryAsync(Document, Document.FilePath, Document.SaveEncodingType);

    [RelayCommand]
    private async Task SaveFileAsAsync() => await PickSaveDestinationAsync(Document);

    private async Task<bool> PickSaveDestinationAsync(DocumentModel original)
    {
        try
        {
            var file = await PickSaveChoiceAsync(original, original.SaveEncodingType);
            return file is { Encoding: { } encoding } && Document == original &&
                await SaveWithRetryAsync(original, file.Path, encoding, createNew: !file.OverwriteConfirmed);
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            await ShowErrorAsync($"Cannot save file. {ex.Message}");
            return false;
        }
    }

    private async Task<FilePickerDialog.Choice?> PickSaveChoiceAsync(DocumentModel original, FileEncodingType encoding,
        string? readOnlyPath = null)
    {
        if (_hardLinkPromptPending || _savePickerPending) return null;
        _savePickerPending = true;
        try
        {
            if (readOnlyPath != null)
            {
                if (OwnerWindow is not { IsVisible: true } owner) return null;
                var answer = await MessageDialog.ShowAsync(owner, "Read-only file",
                    $"{Path.GetFileName(readOnlyPath)} is read-only. Save your edits to another file?",
                    [("Save _As…", DialogChoice.SaveAs), ("Cancel", DialogChoice.Cancel)]);
                if (answer != DialogChoice.SaveAs || Document != original) return null;
            }
            return await PickFileAsync(original, encoding);
        }
        finally { _savePickerPending = false; }
    }

    private Task<FilePickerDialog.Choice?> PickFileAsync(DocumentModel document, FileEncodingType? saveEncoding) =>
        new FilePickerDialog(document.IsUntitled ? null : document.FilePath, RecentFiles.FirstOrDefault(), saveEncoding,
            document.IsUntitled && document.SuggestedExtension is { } extension ? "snippet" + extension : null)
            .ShowDialog<FilePickerDialog.Choice?>(GetMainWindow());

    private async Task<bool> SaveWithRetryAsync(DocumentModel original, string path, FileEncodingType encoding,
        bool createNew = false)
    {
        HardLinkSaveTarget? approvedLink = null;
        try
        {
            while (Document == original)
            {
                try { return await SaveRevisionAsync(original, path, encoding, approvedLink, createNew); }
                catch (Exception ex) when (ex is ReadOnlyFileException || IsAccessDenied(ex))
                {
                    if (Document != original) return false;
                    bool samePath = !original.IsUntitled && SettingsService.PathComparer.Equals(original.FilePath, path);
                    if (samePath)
                        original.IsReadOnlyFile = true;
                    FilePickerDialog.Choice? file;
                    if (OperatingSystem.IsWindows() && samePath && !createNew && approvedLink == null &&
                        original.SavedFileVersion?.ResolvedPath != null && IsAccessDenied(ex) && !ElevatedSave.IsAdministrator())
                    {
                        var answer = await AskAboutAdministratorSaveAsync(path);
                        if (Document != original) return false;
                        if (answer == DialogChoice.SaveAsAdministrator)
                            return await SaveRevisionAsync(original, path, encoding, null, createNew: false, asAdministrator: true);
                        if (answer != DialogChoice.SaveAs) return false;
                        file = await PickSaveChoiceAsync(original, encoding);
                    }
                    else
                    {
                        if (ex is not ReadOnlyFileException) throw;
                        file = await PickSaveChoiceAsync(original, encoding, readOnlyPath: path);
                    }
                    if (file is not { Encoding: { } pickedEncoding }) return false;
                    path = file.Path;
                    encoding = pickedEncoding;
                    createNew = !file.OverwriteConfirmed;
                    approvedLink = null;
                }
                catch (HardLinkSaveRequiredException ex) when (approvedLink == null)
                {
                    if (Document != original) return false;
                    var answer = await AskAboutHardLinksAsync(path);
                    if (Document != original) return false;
                    if (answer == DialogChoice.UpdateAllLinks)
                    {
                        approvedLink = ex.Target;
                        continue;
                    }
                    if (answer != DialogChoice.SaveAs) return false;
                    var file = await PickSaveChoiceAsync(original, encoding);
                    if (file is not { Encoding: { } pickedEncoding }) return false;
                    path = file.Path;
                    encoding = pickedEncoding;
                    createNew = !file.OverwriteConfirmed;
                }
                catch (EncoderFallbackException) when (encoding is FileEncodingType.ANSI or FileEncodingType.OEM437 && Document == original)
                {
                    if (ActiveDialogOwner is not { IsVisible: true } owner) return false;
                    var answer = await MessageDialog.ShowAsync(owner, "Save as UTF-8?",
                        "The selected encoding cannot represent some characters. Use UTF-8 to preserve them?",
                        [("Use _UTF-8", DialogChoice.Yes), ("Cancel", DialogChoice.Cancel)], defaultChoice: DialogChoice.Cancel);
                    if (answer != DialogChoice.Yes) return false;
                    encoding = FileEncodingType.UTF8;
                }
            }
            return false;
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            await ShowErrorAsync($"Cannot save file. {ex.Message}");
            return false;
        }
    }

    private static bool IsAccessDenied(Exception ex) => ex is UnauthorizedAccessException or IOException { HResult: unchecked((int)0x80070005) } ||
        ex.InnerException is UnauthorizedAccessException or Win32Exception { NativeErrorCode: 5 };

    private async Task<DialogChoice> AskAboutAdministratorSaveAsync(string path)
    {
        if (_savePickerPending || OwnerWindow is not { IsVisible: true } owner) return DialogChoice.Cancel;
        _savePickerPending = true;
        try
        {
            return await MessageDialog.ShowAsync(owner, "Administrator permission required",
                $"Windows denied access to {Path.GetFileName(path)}. Save it with administrator permission or choose another file.",
                [("Save as _Administrator", DialogChoice.SaveAsAdministrator), ("Save _As…", DialogChoice.SaveAs), ("Cancel", DialogChoice.Cancel)],
                defaultChoice: DialogChoice.Cancel);
        }
        finally { _savePickerPending = false; }
    }

    private async Task<DialogChoice> AskAboutHardLinksAsync(string path)
    {
        if (_hardLinkPromptPending || OwnerWindow is not { IsVisible: true } owner) return DialogChoice.Cancel;
        _hardLinkPromptPending = true;
        try
        {
            return await MessageDialog.ShowAsync(owner, "Save hard-linked file",
                $"{Path.GetFileName(path)} has multiple hard links. Update All Links saves the changes to each name " +
                "and keeps a recovery copy beside the file until the write succeeds.",
                [("Update _All Links", DialogChoice.UpdateAllLinks), ("Save _As…", DialogChoice.SaveAs), ("Cancel", DialogChoice.Cancel)],
                defaultChoice: DialogChoice.Cancel);
        }
        finally { _hardLinkPromptPending = false; }
    }

    private async Task<bool> SaveRevisionAsync(DocumentModel original, string path, FileEncodingType encoding,
        HardLinkSaveTarget? approvedLink, bool createNew, bool asAdministrator = false)
    {
        if (Document != original || EditorControl is not { } editor) return false;
        long revision = editor.CaptureSaveRevision();
        string content = editor.GetText();
        var lineEnding = original.LineEnding;
        var pendingEncoding = original.SaveEncodingType;
        path = Path.GetFullPath(path);

        _pendingWrites++;
        _saveIdle ??= new(TaskCreationOptions.RunContinuationsAsynchronously);
        await SaveQueue.WaitAsync();
        try
        {
            var expected = !original.IsUntitled && SettingsService.PathComparer.Equals(original.FilePath, path)
                ? original.SavedFileVersion : null;
            var fileVersion = asAdministrator
                ? await ElevatedSave.SaveAsync(_fileService, path, content, encoding, lineEnding, expected ?? throw new FileChangedException())
                : approvedLink is { } approved
                    ? await _fileService.CommitHardLinkedFileAsync(path, content, encoding, expected, approved)
                    : createNew
                        ? await _fileService.CommitNewFileAsync(path, content, encoding)
                        : await _fileService.CommitFileAsync(path, content, encoding, lineEnding, expected);
            if (fileVersion == null) return false;
            if (Document != original || EditorControl != editor) return true;
            StopFollowingFile();
            editor.MarkSaved(revision);
            original.FilePath = path;
            original.IsUntitled = false;
            original.IsReadOnlyFile = asAdministrator;
            original.EncodingType = encoding;
            if (original.SaveEncodingType == pendingEncoding) original.SaveEncodingType = encoding;
            original.Encoding = _fileService.GetEncoding(encoding);
            original.SavedFileVersion = fileVersion;
            UpdateDocumentModified();
            RefreshLanguage();
            NotifyEditorAppearance();
            UpdateRunScriptAvailability();
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
        if (_saveDecisionPending || _savePickerPending || _hardLinkPromptPending) return false;
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
            if (result == DialogChoice.Yes) return await SaveCurrentDocumentAsync() && Document == original && !original.IsModified;
            if (result != DialogChoice.No) return false;
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
        try { path = DocumentPath.GetFullPath(path); }
        catch (Exception ex) when (IsFileError(ex))
        {
            await ShowErrorAsync($"Cannot open file. {ex.Message}");
            return;
        }
        if (!File.Exists(path) && !Path.HasExtension(path)) path += ".txt";
        if (!File.Exists(path))
        {
            var choice = await ShowMessageAsync("Notepad", $"Cannot find {path}. Do you want to create it?", MessageButtons.YesNoCancel);
            if (choice == DialogChoice.Cancel) { OwnerWindow?.Close(); return; }
            if (choice != DialogChoice.Yes) return;
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
