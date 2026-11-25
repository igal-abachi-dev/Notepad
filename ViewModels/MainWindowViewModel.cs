using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using Notepad.NeoEdit;
using NotepadAvalonia.Models;
using NotepadAvalonia.Services;
using NotepadAvalonia.Views;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace NotepadAvalonia.ViewModels;

/// <summary>
/// Main ViewModel - maps to Notepad's global state and command handlers
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    private readonly FileService _fileService;
    private readonly SearchService _searchService;
    private readonly SettingsService _settingsService;
    private readonly PrintService _printService;
    private FindReplaceDialog? _findReplaceDialog;
    private readonly TimeProvider _timeProvider;
    private long _editVersion;
    private string _loadedContent = "";
    internal Window? OwnerWindow { get; set; }

    [ObservableProperty]
    private DocumentModel _document = new();

    [ObservableProperty]
    private EditorSettings _settings = new();

    [ObservableProperty]
    private SearchSettings _searchSettings = new();

    [ObservableProperty]
    private SyntaxLanguage _currentLanguage = SyntaxLanguage.Plain;

    [ObservableProperty]
    private PageSetupSettings _pageSetupSettings = new();

    [ObservableProperty]
    private int _caretLine = 1;

    [ObservableProperty]
    private int _caretColumn = 1;

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private string _encodingText = "UTF-8";

    [ObservableProperty]
    private string _lineEndingText = "CRLF";

    [ObservableProperty]
    private string? _settingsWarning;

    public ObservableCollection<string> RecentFiles { get; }

    // Reference to your custom control
    public NeoEditor? EditorControl { get; set; }

    public MainWindowViewModel(
        FileService fileService,
        SearchService searchService,
        SettingsService settingsService,
        PrintService printService,
        TimeProvider? timeProvider = null,
        DraftStore? draftStore = null)
    {
        _fileService = fileService;
        _searchService = searchService;
        _settingsService = settingsService;
        _printService = printService;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _draftStore = draftStore;
        var persisted = _settingsService.Load();
        Settings = persisted.Editor;
        SearchSettings = persisted.Search;
        PageSetupSettings = persisted.PageSetup;
        Document = CreateUntitledDocument();

        RecentFiles = _settingsService.GetSharedRecents(persisted.RecentFiles);

        OnPropertyChanged(nameof(EditorFontSize));
        OnPropertyChanged(nameof(EditorFontWeight));
        OnPropertyChanged(nameof(EditorFontStyle));
        OnPropertyChanged(nameof(CanUseGoToLine));
        UpdateEncodingDisplay();
    }

    public void InitializeEditor(NeoEditor editor)
    {
        if (EditorControl == editor) return;
        DetachEditor();

        EditorControl = editor;
        editor.TextChanged += OnEditorTextChanged;
        editor.CaretMoved += OnEditorCaretMoved;
        editor.ZoomRequested += OnEditorZoomRequested;
        ResetEditor(_loadedContent);
    }

    internal void DetachEditor()
    {
        if (EditorControl is not { } editor) return;
        _loadedContent = editor.GetText();
        editor.TextChanged -= OnEditorTextChanged;
        editor.CaretMoved -= OnEditorCaretMoved;
        editor.ZoomRequested -= OnEditorZoomRequested;
        EditorControl = null;
    }

    private void ResetEditor(string content)
    {
        _loadedContent = content;
        if (EditorControl == null) return;

        EditorControl.NewLine = Settings.WindowsOnlyLineEndings ? "\r\n" : Document.LineEnding switch
        {
            LineEndingStyle.LF => "\n",
            LineEndingStyle.CR => "\r",
            _ => "\r\n"
        };
        EditorControl.PreservePastedLineEndings = Settings.PreservePastedLineEndings;
        EditorControl.WindowsOnlyLineEndings = Settings.WindowsOnlyLineEndings;
        var restored = _pendingDraftSelection;
        EditorControl.LoadDocument(content, restoredDraft: restored != null);
        if (restored != null)
        {
            _pendingDraftSelection = null;
            int anchor = Math.Clamp(restored.Anchor, 0, EditorControl.TextLength);
            int caret = Math.Clamp(restored.Caret, 0, EditorControl.TextLength);
            EditorControl.SetSelection(anchor, caret - anchor);
            ScheduleDraftCheckpoint();
        }
        if (_pendingLogAppend is { } append)
        {
            _pendingLogAppend = null;
            EditorControl.ReplaceRange(content.Length, 0, append);
        }
    }

    private void OnEditorCaretMoved(object? sender, (int Line, int Column) position)
    {
        UpdateCaretPosition(position.Line, position.Column);
        CutCommand.NotifyCanExecuteChanged();
        CopyCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        SearchWithBingCommand.NotifyCanExecuteChanged();
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        _editVersion++;
        if (EditorControl is { } editor && Document.IsModified != editor.IsModified)
        {
            Document.IsModified = editor.IsModified;
            OnPropertyChanged(nameof(Document));
        }
        ScheduleDraftCheckpoint();
        UpdateStatus();
    }

    private void OnEditorZoomRequested(object? sender, int direction) => SetZoom(Settings.ZoomLevel + direction * 10);

    public double EditorFontSize => Settings.FontSize * 96.0 / 72.0 * Settings.ZoomLevel / 100.0;
    public FontWeight EditorFontWeight => Settings.IsBold ? FontWeight.Bold : FontWeight.Normal;
    public FontStyle EditorFontStyle => Settings.IsItalic ? FontStyle.Italic : FontStyle.Normal;

    // ==================== Edit Commands ====================

    /// <summary>
    /// Maps to: Command ID 768 (EM_UNDO)
    /// </summary>
    private bool CanUndo() => EditorControl?.CanUndo == true;
    private bool CanRedo() => EditorControl?.CanRedo == true;

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo() => EditorControl?.Undo();

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo() => EditorControl?.Redo();

    /// <summary>
    /// Maps to: Command ID 769 (WM_CUT)
    /// </summary>
    private bool HasSelection() => EditorControl?.SelectionLength > 0;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private Task CutAsync() => RunClipboardCommandAsync(editor => editor.CutAsync());

    /// <summary>
    /// Maps to: Command ID 770 (WM_COPY)
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private Task CopyAsync() => RunClipboardCommandAsync(editor => editor.CopyAsync());

    /// <summary>
    /// Maps to: Command ID 771 (WM_PASTE)
    /// </summary>
    [RelayCommand]
    private Task PasteAsync() => RunClipboardCommandAsync(editor => editor.PasteAsync());

    private async Task RunClipboardCommandAsync(Func<NeoEditor, Task> operation)
    {
        if (EditorControl is not { } editor) return;

        try
        {
            await operation(editor);
        }
        catch (Exception ex)
        {
            await ShowErrorAsync($"Cannot access the clipboard: {ex.Message}");
        }
    }

    /// <summary>
    /// Maps to: Command ID 20
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Delete()
    {
        EditorControl?.DeleteSelection();
    }

    /// <summary>
    /// Maps to: Command ID 25 (EM_SETSEL 0, -1)
    /// </summary>
    [RelayCommand]
    private void SelectAll() => EditorControl?.SelectAll();

    /// <summary>
    /// Maps to: Command ID 26
    /// </summary>
    [RelayCommand]
    private void InsertDateTime()
    {
        EditorControl?.InsertAtCaret(FormatTimeDate(_timeProvider.GetLocalNow().DateTime));
    }

    // ==================== Find/Replace ====================

    /// <summary>
    /// Maps to: Command ID 21, function_14001cfac
    /// </summary>
    [RelayCommand]
    private void ShowFindDialog()
    {
        ShowFindOrReplaceDialog(findMode: true);
    }

    /// <summary>
    /// Maps to: Command ID 23
    /// </summary>
    [RelayCommand]
    private void ShowReplaceDialog()
    {
        ShowFindOrReplaceDialog(findMode: false);
    }

    [RelayCommand]
    private async Task ShowPageSetupDialog()
    {
        var dialog = new PageSetupDialog(PageSetupSettings, Settings, Document.FileName, EditorControl?.GetText() ?? _loadedContent);
        var result = await dialog.ShowDialog<PageSetupSettings?>(GetMainWindow());
        if (result != null)
        {
            PageSetupSettings = result;
            PersistSettings();
        }
    }

    [RelayCommand]
    private async Task PrintAsync() => await PrintDocumentAsync(direct: false);

    private async Task<bool> PrintDocumentAsync(bool direct)
    {
        if (EditorControl is not { } editor || OwnerWindow == null) return false;
        if (!OperatingSystem.IsWindows() || OwnerWindow.TryGetPlatformHandle() is not { } handle)
        {
            await ShowErrorAsync("Printing is available on Windows.");
            return false;
        }
        if (!PageSetupSettings.HasValidMargins())
        {
            await ShowErrorAsync("Adjust the margins in Page Setup before printing.");
            return false;
        }
        try
        {
            return await _printService.PrintAsync(handle.Handle, Document.FileName, editor.GetText(),
                PageSetupSettings, Settings, direct);
        }
        catch (Exception ex) when (IsFileError(ex) || ex is Win32Exception)
        {
            await ShowErrorAsync($"Cannot print. {ex.Message}");
            return false;
        }
    }

    [RelayCommand]
    private void Exit() => OwnerWindow?.Close();

    /// <summary>
    /// Maps to: Command ID 28 (F3)
    /// </summary>
    [RelayCommand]
    private Task FindNext() => RepeatFindAsync(reverse: false);

    /// <summary>
    /// Maps to: Shift+F3 (reverse find)
    /// </summary>
    [RelayCommand]
    private Task FindPrevious() => RepeatFindAsync(reverse: true);

    private Task RepeatFindAsync(bool reverse)
    {
        if (!string.IsNullOrEmpty(SearchSettings.SearchString)) return FindCoreAsync(reverse);
        ShowFindOrReplaceDialog(findMode: true);
        return Task.CompletedTask;
    }

    internal Task FindFromDialogAsync(bool findMode) => FindCoreAsync(findMode && SearchSettings.SearchUp);

    private Task FindCoreAsync(bool reverse) => RunSearchAsync(editor =>
    {
        UpdateStatus();
        var (result, wrapped) = reverse
            ? _searchService.FindPrevious(editor, SearchSettings)
            : _searchService.FindNext(editor, SearchSettings);

        if (result != null)
        {
            editor.SetSelection(result.StartOffset, result.Length);
            if (wrapped) StatusText += reverse ? "    Search wrapped to bottom" : "    Search wrapped to top";
            return Task.CompletedTask;
        }

        return ShowErrorAsync($"Cannot find \"{SearchSettings.SearchString}\"");
    });

    [RelayCommand]
    private Task ReplaceNext() => RunSearchAsync(async editor =>
    {
        UpdateStatus();
        int replaced = _searchService.ReplaceNext(editor, SearchSettings, out bool found, out bool wrapped);
        if (wrapped) StatusText += "    Search wrapped";
        if (replaced == 0 && !found)
            await ShowErrorAsync($"Cannot find \"{SearchSettings.SearchString}\"");
    });

    [RelayCommand]
    private Task ReplaceAll() => RunSearchAsync(async editor =>
    {
        int count = _searchService.ReplaceAll(editor, SearchSettings);
        await ShowMessageAsync("Notepad", $"Replaced {count} occurrence(s).", ButtonEnum.Ok);
    });

    private async Task RunSearchAsync(Func<NeoEditor, Task> operation)
    {
        if (EditorControl is not { } editor || string.IsNullOrEmpty(SearchSettings.SearchString)) return;

        try
        {
            await operation(editor);
        }
        catch (RegexParseException ex)
        {
            await ShowErrorAsync($"Invalid regular expression. {ex.Message}");
        }
        catch (RegexMatchTimeoutException)
        {
            await ShowErrorAsync("The regular expression took too long. Try a simpler pattern.");
        }
        catch (ArgumentException ex)
        {
            await ShowErrorAsync($"Cannot apply the search replacement. {ex.Message}");
        }
    }

    /// <summary>
    /// Maps to: Command ID 24 (Go To dialog)
    /// </summary>
    [RelayCommand]
    private async Task GoToLineAsync()
    {
        if (Settings.WordWrap || EditorControl == null) return;
        var dialog = new GoToLineDialog(EditorControl.LineCount, CaretLine);
        var result = await dialog.ShowDialog<int?>(GetMainWindow());

        if (result.HasValue)
        {
            EditorControl.ScrollToLine(result.Value - 1); // 0-based
            EditorControl.MoveCaretToLine(result.Value - 1);
        }
    }


    // --- Misc ---
    private void ShowFindOrReplaceDialog(bool findMode)
    {
        if (OwnerWindow is not { } owner) return;
        if (_findReplaceDialog != null && _findReplaceDialog.DataContext is FindReplaceViewModel current &&
            current.IsFindMode == findMode)
        {
            _findReplaceDialog.Activate();
            _findReplaceDialog.FocusQuery();
            return;
        }
        if (EditorControl?.SelectionLength is > 0 and <= 127)
        {
            string seed = EditorControl.GetSelectedText();
            if (!seed.Contains('\r') && !seed.Contains('\n')) SearchSettings.SearchString = seed;
        }
        if (_findReplaceDialog != null)
        {
            _findReplaceDialog.DataContext = new FindReplaceViewModel(this, findMode);
            _findReplaceDialog.Activate();
            _findReplaceDialog.FocusQuery();
            return;
        }

        _findReplaceDialog = new FindReplaceDialog(this, findMode);
        _findReplaceDialog.Closed += (_, _) => _findReplaceDialog = null;
        _findReplaceDialog.Show(owner);
    }

    // ==================== Format Commands ====================

    /// <summary>
    /// Maps to: Command ID 32, function_14001d364
    /// </summary>
    [RelayCommand]
    private void ToggleWordWrap()
    {
        Settings.WordWrap = !Settings.WordWrap;

        OnPropertyChanged(nameof(Settings));
        OnPropertyChanged(nameof(CanUseGoToLine));
        PersistSettings();
    }

    /// <summary>
    /// Maps to: Command ID 33 (ChooseFontW)
    /// </summary>
    [RelayCommand]
    private async Task ChangeFontAsync()
    {
        var dialog = new FontDialog(Settings);

        var result = await dialog.ShowDialog<bool>(GetMainWindow());
        if (result)
        {
            Settings.FontFamily = dialog.SelectedFamily;
            Settings.FontSize = dialog.SelectedSize;
            Settings.IsBold = dialog.IsBold;
            Settings.IsItalic = dialog.IsItalic;
            OnPropertyChanged(nameof(Settings));
            OnPropertyChanged(nameof(EditorFontSize));
            OnPropertyChanged(nameof(EditorFontWeight));
            OnPropertyChanged(nameof(EditorFontStyle));
            PersistSettings();
        }
    }

    // ==================== View Commands ====================

    /// <summary>
    /// Maps to: Command ID 27
    /// </summary>
    [RelayCommand]
    private void ToggleStatusBar()
    {
        Settings.ShowStatusBar = !Settings.ShowStatusBar;
        OnPropertyChanged(nameof(Settings));
        PersistSettings();
    }

    [RelayCommand]
    private void ToggleLineNumbers()
    {
        Settings.ShowLineNumbers = !Settings.ShowLineNumbers;
        OnPropertyChanged(nameof(Settings));
        PersistSettings();
    }

    [RelayCommand]
    private void ToggleRightToLeft()
    {
        Settings.RightToLeft = !Settings.RightToLeft;
        OnPropertyChanged(nameof(Settings));
        PersistSettings();
    }

    [RelayCommand]
    private void TogglePreservePasteLineEndings()
    {
        Settings.PreservePastedLineEndings = !Settings.PreservePastedLineEndings;
        if (EditorControl != null) EditorControl.PreservePastedLineEndings = Settings.PreservePastedLineEndings;
        OnPropertyChanged(nameof(Settings));
        PersistSettings();
    }

    [RelayCommand]
    private void ToggleWindowsOnlyLineEndings()
    {
        Settings.WindowsOnlyLineEndings = !Settings.WindowsOnlyLineEndings;
        if (EditorControl is { } editor)
        {
            editor.WindowsOnlyLineEndings = Settings.WindowsOnlyLineEndings;
            editor.NewLine = Settings.WindowsOnlyLineEndings || Document.LineEnding == LineEndingStyle.CRLF
                ? "\r\n" : Document.LineEnding == LineEndingStyle.LF ? "\n" : "\r";
        }
        OnPropertyChanged(nameof(Settings));
        PersistSettings();
    }

    [RelayCommand]
    private void ZoomIn() => SetZoom(Settings.ZoomLevel + 10);

    [RelayCommand]
    private void ZoomOut() => SetZoom(Settings.ZoomLevel - 10);

    [RelayCommand]
    private void RestoreZoom() => SetZoom(100);

    private void SetZoom(int level)
    {
        level = Math.Clamp(level, 10, 500);
        if (level == Settings.ZoomLevel) return;
        Settings.ZoomLevel = level;
        OnPropertyChanged(nameof(Settings));
        OnPropertyChanged(nameof(EditorFontSize));
        PersistSettings();
    }

    // ==================== Help Commands ====================

    [RelayCommand]
    private Task ViewHelp() => LaunchAsync("https://go.microsoft.com/fwlink/?LinkId=834783");

    public bool IsFeedbackAvailable => OperatingSystem.IsWindows();

    [RelayCommand]
    private Task SendFeedback() => LaunchAsync("feedback-hub://?tabid=2&contextid=1010");

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private Task SearchWithBing()
    {
        if (EditorControl is not { } editor || !HasSelection()) return Task.CompletedTask;
        if (editor.SelectionLength > 2048) return ShowErrorAsync("Select up to 2,048 characters to search the web.");
        return LaunchAsync("https://www.bing.com/search?q=" + Uri.EscapeDataString(editor.GetSelectedText()));
    }

    private async Task LaunchAsync(string address)
    {
        try { Process.Start(new ProcessStartInfo { FileName = address, UseShellExecute = true }); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            await ShowErrorAsync($"Cannot open the link. {ex.Message}");
        }
    }

    [RelayCommand]
    private void ShowAbout()
    {
        var dialog = new AboutDialog();
        dialog.ShowDialog(GetMainWindow());
    }

    public void UpdateCaretPosition(int line, int column)
    {
        CaretLine = line;
        CaretColumn = column;
        UpdateStatus();
    }

    private void UpdateEncodingDisplay()
    {
        EncodingText = Document.EncodingType switch
        {
            FileEncodingType.ANSI => "Windows (ANSI)",
            FileEncodingType.UTF16LE => "Unicode",
            FileEncodingType.UTF16BE => "Unicode big endian",
            FileEncodingType.UTF8BOM => "UTF-8 with BOM",
            FileEncodingType.UTF8 => "UTF-8",
            _ => Document.Encoding.WebName.ToUpperInvariant()
        };
        LineEndingText = Document.LineEnding switch
        {
            LineEndingStyle.CR => "CR",
            LineEndingStyle.LF => "LF",
            _ => "CRLF"
        };
    }

    private void UpdateStatus()
    {
        var lines = EditorControl?.LineCount ?? 1;
        StatusText = $"Ln {CaretLine}, Col {CaretColumn}    Lines: {lines}";
    }

    public void SaveWindowPlacement(PixelPoint position, Size size)
    {
        Settings.WindowX = position.X;
        Settings.WindowY = position.Y;
        Settings.WindowWidth = (int)size.Width;
        Settings.WindowHeight = (int)size.Height;
    }

    public void SaveSessionSettings() => PersistSettings();

    private void PersistSettings()
    {
        try
        {
            _settingsService.Save(new AppSettings
            {
                Editor = Settings, Search = SearchSettings, PageSetup = PageSetupSettings, RecentFiles = [.. RecentFiles]
            });
            SettingsWarning = null;
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            Trace.TraceWarning("Could not save the Notepad preferences: {0}", ex.GetType().Name);
            SettingsWarning = "Preferences could not be saved.";
        }
    }

    public bool CanUseGoToLine => !Settings.WordWrap;

    private void AddRecentFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        var existing = RecentFiles.FirstOrDefault(item => SettingsService.PathComparer.Equals(item, path));
        if (existing != null) RecentFiles.Remove(existing);

        RecentFiles.Insert(0, path);
        while (RecentFiles.Count > 10)
        {
            RecentFiles.RemoveAt(RecentFiles.Count - 1);
        }
        if (OperatingSystem.IsWindows() && OwnerWindow?.TryGetPlatformHandle()?.HandleDescriptor == "HWND")
            SHAddToRecentDocs(3, path); // SHARD_PATHW
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern void SHAddToRecentDocs(uint kind, string path);

    private Window GetMainWindow() => OwnerWindow ??
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow ??
        throw new InvalidOperationException("The command requires a window.");

    private Window? ActiveDialogOwner => _findReplaceDialog is { IsVisible: true, IsActive: true } ? _findReplaceDialog : OwnerWindow;

    private async Task<ButtonResult> ShowMessageAsync(string title, string message, ButtonEnum buttons)
    {
        if (ActiveDialogOwner is { IsVisible: true } owner)
            return await MessageBoxManager.GetMessageBoxStandard(title, message, buttons).ShowWindowDialogAsync(owner);

        return ButtonResult.None;
    }

    private Task ShowErrorAsync(string message) => ShowMessageAsync("Notepad", message, ButtonEnum.Ok);
}
