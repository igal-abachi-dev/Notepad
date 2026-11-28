using CommunityToolkit.Mvvm.Input;
using NotepadAvalonia.Models;
using NotepadAvalonia.Services;
using NotepadAvalonia.Services.Tools;
using NotepadAvalonia.Views;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml;

namespace NotepadAvalonia.ViewModels;

public partial class MainWindowViewModel
{
    private CommentSyntax ActiveCommentSyntax => CommentSyntax.ForLanguage(CurrentLanguage, Document.FilePath);

    private bool CanToggleComments() => !IsEditorReadOnly && EditorControl is { IsReadOnly: false, HasColumnSelection: false } &&
        ActiveCommentSyntax is { Line: not null } or { BlockStart: not null };

    [RelayCommand(CanExecute = nameof(CanToggleComments))]
    private void ToggleLineComment() => ToggleComments(preferBlock: false);

    [RelayCommand(CanExecute = nameof(CanToggleComments))]
    private void ToggleBlockComment() => ToggleComments(preferBlock: true);

    private void ToggleComments(bool preferBlock)
    {
        if (!CanToggleComments() || EditorControl is not { } editor) return;
        var syntax = ActiveCommentSyntax;
        if ((preferBlock || syntax.Line == null) && syntax.BlockStart is { } opening && syntax.BlockEnd is { } closing)
        {
            if (!editor.ToggleBlockComment(opening, closing, useWholeLines: !preferBlock))
            {
                UpdateStatus();
                StatusText += "    This selection cannot be wrapped in a block comment.";
            }
        }
        else if (syntax.Line is { } marker) editor.ToggleLineComments(marker);
    }

    private void NotifyCommentAvailability()
    {
        ToggleLineCommentCommand.NotifyCanExecuteChanged();
        ToggleBlockCommentCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task InspectCharactersAsync()
    {
        if (EditorControl is not { } editor) return;
        var (text, selection, truncated) = editor.GetCharacterSample();
        await ShowMessageAsync("Unicode Character Inspector", CharacterInspector.Describe(text, selection, truncated), MessageButtons.Ok);
    }

    public bool CanRunScript => OperatingSystem.IsWindows() &&
        ScriptLaunch.IsScriptPath(Document.IsUntitled ? Document.SuggestedExtension ?? "" : Document.FilePath);

    private void UpdateRunScriptAvailability()
    {
        OnPropertyChanged(nameof(CanRunScript));
        RunScriptCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanRunScript))]
    private async Task RunScriptAsync()
    {
        if (!CanRunScript || HasPendingSave || EditorControl is not { } editor) return;
        var document = Document;
        long revision = editor.DocumentVersion;
        if (document.IsUntitled || document.IsModified)
        {
            if (OwnerWindow is not { IsVisible: true } owner) return;
            var answer = await MessageDialog.ShowAsync(owner, "Save before running", "Save the script before running it?",
                [("Save and _Run", DialogChoice.Yes), ("Cancel", DialogChoice.Cancel)], defaultChoice: DialogChoice.Cancel);
            if (answer != DialogChoice.Yes || Document != document || editor.DocumentVersion != revision ||
                !await SaveCurrentDocumentAsync()) return;
        }
        if (Document != document || document.IsUntitled || document.IsModified || editor.DocumentVersion != revision ||
            !CanRunScript || document.SavedFileVersion is not { } version) return;
        string path = document.FilePath;

        try
        {
            using var verified = await _fileService.OpenVerifiedReadAsync(path, version);
            if (Document != document || document.FilePath != path || document.IsModified || editor.DocumentVersion != revision) return;
            var start = ScriptLaunch.CreateStartInfo(path, Environment.SystemDirectory,
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
            using var console = Process.Start(start);
            if (console == null) throw new IOException("The script console did not start.");
        }
        catch (FileChangedException)
        {
            await ShowErrorAsync("The script changed or was removed on disk. Reopen it before running it.");
        }
        catch (Exception ex) when (IsFileError(ex) || ex is Win32Exception or InvalidOperationException)
        {
            await ShowErrorAsync($"Cannot run the script. {ex.Message}");
        }
    }

    [RelayCommand(CanExecute = nameof(CanCopySelectionToNewWindow))]
    private async Task CopySelectionToNewWindowAsync()
    {
        if (_draftStore is not { } store || EditorControl is not { HasTextSelection: true } editor) return;
        string selected = editor.GetSelectedText();
        var encoding = Settings.DefaultEncoding;
        bool windowsOnly = Settings.WindowsOnlyLineEndings;
        try
        {
            var snapshot = await Task.Run(() =>
            {
                var (content, extension) = FencedSelection.Extract(selected);
                return new DraftSnapshot(content, null, encoding, windowsOnly ? LineEndingStyle.CRLF : _fileService.DetectLineEndings(content),
                    0, 0, null, _timeProvider.GetUtcNow()) { SuggestedExtension = extension };
            });
            var lease = await Task.Run(store.Create);
            try { await lease.WriteAsync(snapshot, 0); }
            finally { await lease.ReleaseAsync(); }
            await LaunchNewProcessAsync("--recover-draft", lease.Id.ToString("N"));
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            await ShowErrorAsync($"Cannot copy the selection to a new window. {ex.Message}");
        }
    }

    private bool CanCopySelectionToNewWindow() => SupportsDraftRecovery && HasTextSelection;

    [RelayCommand(CanExecute = nameof(CanOpenSelectionInBrowser))]
    private async Task OpenSelectionInBrowserAsync(string? name)
    {
        if (OwnerWindow is not { } owner || EditorControl is not { HasTextSelection: true } editor) return;
        if (!Enum.TryParse<SelectionWebAction>(name ?? nameof(SelectionWebAction.Search), out var action) || !Enum.IsDefined(action)) return;
        if (editor.SelectionLength > SelectionWebLink.MaxSelectionLength)
        {
            await ShowErrorAsync("Select a shorter excerpt to open in the browser.");
            return;
        }

        string text = editor.GetSelectedText();
        if (string.IsNullOrWhiteSpace(text)) return;
        var uri = SelectionWebLink.Create(action, text, CultureInfo.CurrentUICulture);
        if (uri == null)
        {
            await ShowErrorAsync("Select a shorter excerpt to open in the browser.");
            return;
        }

        bool opened;
        try { opened = await owner.Launcher.LaunchUriAsync(uri); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or NotSupportedException)
        {
            // A launcher exception can include the full URL and its selected text.
            opened = false;
        }
        if (!opened) await ShowErrorAsync("Cannot open the default browser.");
    }

    private bool CanOpenSelectionInBrowser(string? _) => HasTextSelection;

    [RelayCommand]
    private async Task ApplyTextToolAsync(string? name)
    {
        if (EditorControl is not { IsReadOnly: false, HasColumnSelection: false } editor ||
            !Enum.TryParse<TextTool>(name, out var tool) || !Enum.IsDefined(tool)) return;
        if (!HasTextSelection && tool is TextTool.UpperCase or TextTool.LowerCase or TextTool.TitleCase or TextTool.CamelCase) return;
        var document = Document;
        long revision = editor.DocumentVersion;
        long selection = editor.SelectionVersion;
        bool hasSelection = editor.SelectionLength != 0;
        int start = hasSelection ? editor.SelectionStart : 0;
        string input = hasSelection ? editor.GetSelectedText() : editor.GetText();
        string newline = editor.NewLine;
        try
        {
            string output = await Task.Run(() => TextTools.Apply(tool, input, newline));
            if (IsCurrent() && !editor.IsReadOnly && input != output)
                editor.ReplaceRange(start, input.Length, output);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidOperationException or JsonException or XmlException)
        {
            if (IsCurrent()) await ShowErrorAsync($"Cannot transform the text. {ex.Message}");
        }

        bool IsCurrent() => Document == document && EditorControl == editor &&
            editor.DocumentVersion == revision && editor.SelectionVersion == selection;
    }

    [RelayCommand]
    private void ConvertFileEncoding(FileEncodingType encoding)
    {
        if (IsEditorReadOnly) return;
        var original = Document;
        if (original.SaveEncodingType == encoding) return;
        original.SaveEncodingType = encoding;
        if (original.IsUntitled)
        {
            original.EncodingType = encoding;
            original.Encoding = _fileService.GetEncoding(encoding);
        }
        UpdateDocumentModified();
        UpdateEncodingDisplay();
        original.DraftVersion = -1;
        ScheduleDraftCheckpoint();
    }

    [RelayCommand]
    private void ToggleShowAllCharacters()
    {
        Settings.ShowAllCharacters = !Settings.ShowAllCharacters;
        PersistSettings();
    }
}
