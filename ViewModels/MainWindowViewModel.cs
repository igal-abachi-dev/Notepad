using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Notepad.NeoEdit;
using NotepadAvalonia.Models;
using NotepadAvalonia.Services;
using NotepadAvalonia.Views;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
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
    [NotifyPropertyChangedFor(nameof(LanguageLabel))]
    private SyntaxLanguage _currentLanguage = SyntaxLanguage.Plain;

    private SyntaxLanguage? _languageOverride;
    private SyntaxLanguage _untitledLanguage;
    public string LanguageLabel => LanguageName(CurrentLanguage);
    public string AutomaticLanguageLabel => $"Automatic ({LanguageName(DetectedLanguage)})";
    private SyntaxLanguage DetectedLanguage => !Document.IsUntitled ? SyntaxMatcher.DetectLanguage(Document.FilePath) :
        Document.SuggestedExtension is { } extension ? SyntaxMatcher.DetectLanguage(extension) : _untitledLanguage;

    private static string LanguageName(SyntaxLanguage language) => language switch
    {
        SyntaxLanguage.Plain => "Plain Text",
        SyntaxLanguage.CSharp => "C#",
        SyntaxLanguage.JavaScript => "JavaScript",
        SyntaxLanguage.Json => "JSON",
        SyntaxLanguage.Xml => "XML",
        SyntaxLanguage.Html => "HTML",
        SyntaxLanguage.Sql => "SQL",
        SyntaxLanguage.Cpp => "C++",
        SyntaxLanguage.Css => "CSS",
        SyntaxLanguage.Ini => "INI",
        SyntaxLanguage.Toml => "TOML",
        SyntaxLanguage.Yaml => "YAML",
        SyntaxLanguage.Log => "Structured Log",
        _ => language.ToString()
    };

    [RelayCommand]
    private void SetLanguage(SyntaxLanguage? language)
    {
        if (language is { } choice && !Enum.IsDefined(choice)) return;
        _languageOverride = language;
        RefreshLanguage();
    }

    partial void OnDocumentChanged(DocumentModel value)
    {
        StopFollowingFile();
        _languageOverride = null;
        _untitledLanguage = SyntaxLanguage.Plain;
        RefreshLanguage();
        _artWordWrap = null;
        _artFontFamily = null;
        NotifyEditorAppearance();
        UpdateRunScriptAvailability();
    }

    private void RefreshLanguage()
    {
        CurrentLanguage = _languageOverride ?? DetectedLanguage;
        OnPropertyChanged(nameof(AutomaticLanguageLabel));
        NotifyCommentAvailability();
    }

    private void UpdateUntitledLanguage()
    {
        if (Document is not { IsUntitled: true, SuggestedExtension: null } || EditorControl is not { } editor) return;
        var language = SyntaxMatcher.DetectUntitledLanguage(editor.GetTextPrefix(256));
        if (language == _untitledLanguage) return;
        _untitledLanguage = language;
        RefreshLanguage();
    }

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
        _settingsService.TrackChanges(persisted);
        Settings = persisted.Editor;
        SearchSettings = persisted.Search;
        PageSetupSettings = persisted.PageSetup;
        Document = CreateUntitledDocument();

        RecentFiles = _settingsService.GetSharedRecents(persisted.RecentFiles);

        OnPropertyChanged(nameof(EditorFontSize));
        OnPropertyChanged(nameof(EditorFontWeight));
        OnPropertyChanged(nameof(EditorFontStyle));
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
        editor.WebLinkRequested += OnEditorWebLinkRequested;
        editor.ScrollInvalidated += OnSearchViewportChanged;
        ResetEditor(_loadedContent);
        NotifyEditingAvailability();
    }

    internal void DetachEditor()
    {
        if (EditorControl is not { } editor) return;
        StopFollowingFile();
        _loadedContent = editor.GetText();
        editor.TextChanged -= OnEditorTextChanged;
        editor.CaretMoved -= OnEditorCaretMoved;
        editor.ZoomRequested -= OnEditorZoomRequested;
        editor.WebLinkRequested -= OnEditorWebLinkRequested;
        editor.ScrollInvalidated -= OnSearchViewportChanged;
        StopSearchPreview();
        EditorControl = null;
        NotifyEditingAvailability();
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
        EditorControl.LoadDocument(content, restoredDraft: restored != null || (Document.IsUntitled && Document.IsModified));
        UpdateUntitledLanguage();
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
        QueueSearchPreview();
    }

    private void OnEditorCaretMoved(object? sender, (int Line, int Column) position)
    {
        UpdateCaretPosition(position.Line, position.Column);
        CutCommand.NotifyCanExecuteChanged();
        CopyCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        OpenSelectionInBrowserCommand.NotifyCanExecuteChanged();
        CopySelectionToNewWindowCommand.NotifyCanExecuteChanged();
        NotifyCommentAvailability();
        OnPropertyChanged(nameof(HasTextSelection));
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        _editVersion++;
        UpdateUntitledLanguage();
        UpdateDocumentModified();
        if (!Settings.WindowsOnlyLineEndings && EditorControl is { } active)
            SetDocumentLineEnding(active.NewLine);
        ScheduleDraftCheckpoint();
        UpdateStatus();
        QueueSearchPreview();
    }

    private void UpdateDocumentModified()
    {
        if (EditorControl is not { } editor) return;
        bool modified = editor.IsModified || Document.EncodingType != Document.SaveEncodingType;
        if (Document.IsModified == modified) return;
        Document.IsModified = modified;
        OnPropertyChanged(nameof(Document));
    }

    private void OnEditorZoomRequested(object? sender, int direction) => SetZoom(Settings.ZoomLevel + direction * 10);

    private async void OnEditorWebLinkRequested(object? sender, Uri uri) => await LaunchAsync(uri.AbsoluteUri);

    public double EditorFontSize => Settings.FontSize * 96.0 / 72.0 * Settings.ZoomLevel / 100.0;
    public bool IsSystemTheme => Settings.Theme == EditorTheme.System;
    public bool IsLightTheme => Settings.Theme == EditorTheme.Light;
    public bool IsDarkTheme => Settings.Theme == EditorTheme.Dark;

    [RelayCommand]
    private void SetTheme(EditorTheme theme)
    {
        if (!Enum.IsDefined(theme)) return;
        Settings.Theme = theme;
        ApplyTheme();
        OnPropertyChanged(nameof(IsSystemTheme));
        OnPropertyChanged(nameof(IsLightTheme));
        OnPropertyChanged(nameof(IsDarkTheme));
        PersistSettings();
    }

    internal void ApplyTheme()
    {
        if (Application.Current is { } app)
            app.RequestedThemeVariant = Settings.Theme switch
            {
                EditorTheme.Light => ThemeVariant.Light,
                EditorTheme.Dark => ThemeVariant.Dark,
                _ => ThemeVariant.Default
            };
    }
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
    private bool HasSelection() => EditorControl?.HasTextSelection == true;

    public bool HasTextSelection => HasSelection();

    private bool CanEditSelection() => !IsEditorReadOnly && HasSelection();
    private bool CanPaste() => !IsEditorReadOnly && EditorControl != null;

    [RelayCommand(CanExecute = nameof(CanEditSelection))]
    private Task CutAsync() => RunClipboardCommandAsync(editor => editor.CutAsync());

    /// <summary>
    /// Maps to: Command ID 770 (WM_COPY)
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private Task CopyAsync() => RunClipboardCommandAsync(editor => editor.CopyAsync());

    /// <summary>
    /// Maps to: Command ID 771 (WM_PASTE)
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanPaste))]
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
    [RelayCommand(CanExecute = nameof(CanEditSelection))]
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

    [RelayCommand]
    private void InsertGuid()
    {
        EditorControl?.InsertAtCaret(Guid.NewGuid().ToString("D"));
    }

    [RelayCommand]
    private void ReplaceNulsWithSpaces()
    {
        if (EditorControl is not { IsReadOnly: false } editor) return;
        bool replaced = editor.ReplaceNulsWithSpaces();
        UpdateStatus();
        StatusText += replaced ? "    Replaced NUL characters with spaces." : "    No NUL characters found.";
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

    internal Task FindFromDialogAsync(bool reverse) => FindCoreAsync(reverse);

    private Task FindCoreAsync(bool reverse) => RunSearchAsync(editor =>
    {
        var (result, wrapped) = reverse
            ? _searchService.FindPrevious(editor, SearchSettings)
            : _searchService.FindNext(editor, SearchSettings);

        if (result != null)
        {
            if (SearchSettings.HighlightMatches) editor.SelectSearchMatch(result.StartOffset, result.Length);
            else editor.SetSelection(result.StartOffset, result.Length);
            if (wrapped) ReportSearchFeedback(reverse ? "Search wrapped to bottom." : "Search wrapped to top.");
            return;
        }

        editor.ClearActiveSearchMatch();
        ReportSearchFeedback("No matches found.");
    });

    [RelayCommand]
    private Task ReplaceNext() => RunSearchAsync(editor =>
    {
        if (editor.IsReadOnly) { ReportSearchFeedback("Read-only mode is on."); return; }
        int replaced = _searchService.ReplaceNext(editor, SearchSettings, out bool found, out bool wrapped,
            out bool followUpTimedOut);
        if (followUpTimedOut)
            ReportSearchFeedback("Replaced the current match. The search for the next match took too long.");
        else if (replaced == 0 && !found)
            ReportSearchFeedback("No matches found.");
        else if (wrapped)
            ReportSearchFeedback("Search wrapped.");
    });

    [RelayCommand]
    private Task ReplaceAll() => RunSearchAsync(editor =>
    {
        if (editor.IsReadOnly) { ReportSearchFeedback("Read-only mode is on."); return; }
        int count = _searchService.ReplaceAll(editor, SearchSettings);
        ReportSearchFeedback(count switch
        {
            0 => "No matches found.",
            1 => "Replaced 1 occurrence.",
            _ => $"Replaced {count} occurrences."
        });
    });

    private Task RunSearchAsync(Action<NeoEditor> operation)
    {
        if (EditorControl is not { } editor || string.IsNullOrEmpty(SearchSettings.SearchString)) return Task.CompletedTask;

        UpdateStatus();
        ReportSearchFeedback(null);
        try
        {
            operation(editor);
        }
        catch (RegexParseException ex)
        {
            ReportSearchFeedback($"Invalid regular expression. {ex.Message}");
        }
        catch (RegexMatchTimeoutException)
        {
            ReportSearchFeedback("The regular expression took too long. Try a simpler pattern.");
        }
        catch (ArgumentException ex)
        {
            ReportSearchFeedback($"Cannot apply the search replacement. {ex.Message}");
        }
        finally
        {
            QueueSearchPreview();
        }
        return Task.CompletedTask;
    }

    private void ReportSearchFeedback(string? message)
    {
        if (_findReplaceDialog?.DataContext is FindReplaceViewModel dialog)
            dialog.SearchFeedback = message;
        if (message != null)
            StatusText += $"    {message}";
    }

    private bool? _artWordWrap;
    private string? _artFontFamily;
    private bool ShowAnsiArt => !Document.IsUntitled && DocumentPath.IsAnsiArt(Document.FilePath);
    public bool EditorWordWrap => ShowAnsiArt ? _artWordWrap ?? false : Settings.WordWrap;
    public string EditorFontFamily => ShowAnsiArt ? _artFontFamily ?? "Consolas, Lucida Console, DejaVu Sans Mono, Menlo, monospace" : Settings.FontFamily;
    public bool CanUseGoToLine => !EditorWordWrap;

    partial void OnSettingsChanged(EditorSettings? oldValue, EditorSettings newValue)
    {
        if (oldValue != null) oldValue.PropertyChanged -= OnEditorSettingsPropertyChanged;
        newValue.PropertyChanged += OnEditorSettingsPropertyChanged;
        NotifyEditorAppearance();
    }

    private void OnEditorSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EditorSettings.WordWrap) or nameof(EditorSettings.FontFamily))
            NotifyEditorAppearance();
    }

    private void NotifyEditorAppearance()
    {
        OnPropertyChanged(nameof(EditorWordWrap));
        OnPropertyChanged(nameof(EditorFontFamily));
        OnPropertyChanged(nameof(CanUseGoToLine));
        GoToLineCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Maps to: Command ID 24 (Go To dialog)
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanUseGoToLine))]
    private async Task GoToLineAsync()
    {
        if (!CanUseGoToLine || EditorControl is not { } editor) return;
        var dialog = new GoToLineDialog(editor.LineCount, CaretLine);
        var result = await dialog.ShowDialog<int?>(GetMainWindow());

        if (result.HasValue && EditorControl == editor && CanUseGoToLine)
        {
            editor.ScrollToLine(result.Value - 1);
            editor.MoveCaretToLine(result.Value - 1);
        }
    }


    // --- Misc ---
    private void ShowFindOrReplaceDialog(bool findMode)
    {
        if (OwnerWindow is not { } owner) return;
        string? seed = null;
        if (EditorControl?.SelectionLength is > 0 and <= 127)
        {
            string selected = EditorControl.GetSelectedText();
            if (!selected.AsSpan().ContainsAny('\r', '\n')) seed = selected;
        }
        if (_findReplaceDialog != null && _findReplaceDialog.DataContext is FindReplaceViewModel current &&
            current.IsFindMode == findMode)
        {
            if (seed != null) current.FindText = seed;
            _findReplaceDialog.Activate();
            _findReplaceDialog.FocusQuery();
            return;
        }
        if (seed != null)
        {
            SearchSettings.SearchString = seed;
            RefreshSearchPreview();
        }
        if (_findReplaceDialog != null)
        {
            _findReplaceDialog.DataContext = new FindReplaceViewModel(this, findMode);
            _findReplaceDialog.Activate();
            _findReplaceDialog.FocusQuery();
            return;
        }

        _findReplaceDialog = new FindReplaceDialog(this, findMode);
        _findReplaceDialog.Closed += (_, _) =>
        {
            _findReplaceDialog = null;
            StopSearchPreview();
        };
        _findReplaceDialog.Show(owner);
        QueueSearchPreview();
    }

    // ==================== Format Commands ====================

    /// <summary>
    /// Maps to: Command ID 32, function_14001d364
    /// </summary>
    [RelayCommand]
    private void ToggleWordWrap()
    {
        if (ShowAnsiArt)
        {
            _artWordWrap = !EditorWordWrap;
            NotifyEditorAppearance();
            return;
        }
        Settings.WordWrap = !Settings.WordWrap;

        OnPropertyChanged(nameof(Settings));
        PersistSettings();
    }

    [RelayCommand]
    private void ToggleAutoIndent()
    {
        Settings.AutoIndent = !Settings.AutoIndent;
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
            if (ShowAnsiArt) _artFontFamily = dialog.SelectedFamily;
            else Settings.FontFamily = dialog.SelectedFamily;
            Settings.FontSize = dialog.SelectedSize;
            Settings.IsBold = dialog.IsBold;
            Settings.IsItalic = dialog.IsItalic;
            Settings.EnableFontLigatures = dialog.EnableFontLigatures;
            OnPropertyChanged(nameof(Settings));
            OnPropertyChanged(nameof(EditorFontSize));
            OnPropertyChanged(nameof(EditorFontWeight));
            OnPropertyChanged(nameof(EditorFontStyle));
            NotifyEditorAppearance();
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
    private void ToggleUnicodeControls()
    {
        Settings.ShowUnicodeControls = !Settings.ShowUnicodeControls;
        PersistSettings();
    }

    [RelayCommand]
    private void InsertUnicodeControl(string? character)
    {
        if (character?.Length == 1 && NeoEditor.UnicodeControlName(character[0]) != null)
            EditorControl?.InsertAtCaret(character);
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
    private void ConvertLineEndings(LineEndingStyle style)
    {
        if (EditorControl is not { } editor) return;
        if (Settings.WindowsOnlyLineEndings && style != LineEndingStyle.CRLF)
            ToggleWindowsOnlyLineEndings();
        string ending = style switch { LineEndingStyle.LF => "\n", LineEndingStyle.CR => "\r", _ => "\r\n" };
        editor.ConvertLineEndings(ending);
        SetDocumentLineEnding(ending);
        ScheduleDraftCheckpoint();
    }

    private void SetDocumentLineEnding(string ending)
    {
        var style = ending switch { "\n" => LineEndingStyle.LF, "\r" => LineEndingStyle.CR, _ => LineEndingStyle.CRLF };
        if (Document.LineEnding == style) return;
        Document.LineEnding = style;
        Document.DraftVersion = -1;
        LineEndingText = style.ToString();
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

    internal const string ProjectWebsite = "https://github.com/igal-abachi-dev/Notepad";

    [RelayCommand]
    private Task ViewHelp() => LaunchAsync(ProjectWebsite);

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
        EncodingText = Document.SaveEncodingType switch
        {
            FileEncodingType.ANSI => "Windows (ANSI)",
            FileEncodingType.UTF16LE => "Unicode",
            FileEncodingType.UTF16BE => "Unicode big endian",
            FileEncodingType.UTF8BOM => "UTF-8 with BOM",
            FileEncodingType.UTF8 => "UTF-8",
            FileEncodingType.OEM437 => "OEM 437 (DOS / NFO)",
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
        int lines = EditorControl?.LineCount ?? 1;
        int position = EditorControl?.CaretOffset ?? 0;
        StatusText = $"Ln {CaretLine}, Col {CaretColumn} lines:{lines} (Pos {position})";
        if (IsFollowingLog) StatusText += "    Tailing file";
        else if (IsReadOnlyMode) StatusText += "    Read-only";
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
            WindowsRecentDocuments.Add(path);
    }

    private Window GetMainWindow() => OwnerWindow ??
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow ??
        throw new InvalidOperationException("The command requires a window.");

    private Window? ActiveDialogOwner => _findReplaceDialog is { IsVisible: true, IsActive: true } ? _findReplaceDialog : OwnerWindow;

    private async Task<DialogChoice> ShowMessageAsync(string title, string message, MessageButtons buttons)
    {
        if (ActiveDialogOwner is { IsVisible: true } owner)
            return await MessageDialog.ShowAsync(owner, title, message, buttons);

        return DialogChoice.None;
    }

    private Task ShowErrorAsync(string message) => ShowMessageAsync("Notepad", message, MessageButtons.Ok);
}
