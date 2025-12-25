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
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
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
    private bool _statusBarBeforeWrap = true;

    [ObservableProperty]
    private DocumentModel _document = new();

    [ObservableProperty]
    private EditorSettings _settings = new();

    [ObservableProperty]
    private SearchSettings _searchSettings = new();

    //[ObservableProperty]
    //private TextDocument _textDocument = new(/*"hello World".ToCharArray()*/);

    [ObservableProperty]
    private string _fileContent = ""; 
    
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

    public ObservableCollection<string> RecentFiles { get; } = new();

    // Reference to your custom control
    public NeoEditor? EditorControl { get; set; }

    public MainWindowViewModel(
        FileService fileService,
        SearchService searchService,
        SettingsService settingsService,
        PrintService printService)
    {
        _fileService = fileService;
        _searchService = searchService;
        _settingsService = settingsService;
        _printService = printService;

        _settings = _settingsService.LoadEditorSettings();
        _searchSettings = _settingsService.LoadSearchSettings();
        _pageSetupSettings = _settingsService.LoadPageSetupSettings();
        _statusBarBeforeWrap = _settings.ShowStatusBar;
        if (_settings.WordWrap)
        {
            _settings.ShowStatusBar = false;
        }
        Document.Encoding = _fileService.GetEncoding(_settings.LastEncoding);
        Document.EncodingType = _settings.LastEncoding;
        Document.SaveEncodingType = _settings.LastEncoding;

        foreach (var path in _settingsService.LoadRecentFiles())
        {
            RecentFiles.Add(path);
        }

        OnPropertyChanged(nameof(EditorFontSize));
        OnPropertyChanged(nameof(EditorFontWeight));
        OnPropertyChanged(nameof(EditorFontStyle));
        OnPropertyChanged(nameof(CanUseGoToLine));
        OnPropertyChanged(nameof(CanToggleStatusBar));
        UpdateEncodingDisplay();
    }

    public void InitializeEditor(NeoEditor editor)
    {
        EditorControl = editor;

        // Subscribe to NeoEditor events
        EditorControl.TextChanged += OnEditorTextChanged;
        EditorControl.CaretMoved += (s, pos) => UpdateCaretPosition(pos.Line, pos.Column);
    }

    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        if (!Document.IsModified)
        {
            Document.IsModified = true;
            OnPropertyChanged(nameof(Document));
        }
        UpdateStatus();
    }

    public double EditorFontSize => Settings.FontSize * Settings.ZoomLevel / 100.0;
    public FontWeight EditorFontWeight => Settings.IsBold ? FontWeight.Bold : FontWeight.Normal;
    public FontStyle EditorFontStyle => Settings.IsItalic ? FontStyle.Italic : FontStyle.Normal;
    public bool CanToggleStatusBar => !Settings.WordWrap;

    // --- Commands ---

    /// <summary>
    /// Maps to: Command ID 1, function_14000fe24(1)
    /// </summary>
    [RelayCommand]
    private async Task NewFileAsync()
    {
        if (!await CheckSaveChangesAsync()) return;

        FileContent = ""; // Clears the editor via OneWay binding
        CurrentLanguage = SyntaxLanguage.Plain; // Reset to Plain
        Document = new DocumentModel
        {
            Encoding = _fileService.GetEncoding(_settings.LastEncoding),
            EncodingType = _settings.LastEncoding,
            SaveEncodingType = _settings.LastEncoding
        };
        UpdateEncodingDisplay();
        UpdateStatus();
        UpdateCaretPosition(1, 1);
    }

    /// <summary>
    /// Maps to: Command ID 2, function_140008c18
    /// </summary>
    [RelayCommand]
    public async Task OpenFileAsync(string? path = null)
    {
        if (!await CheckSaveChangesAsync()) return;

        if (!string.IsNullOrEmpty(path))
        {
            await LoadFileAsync(path);
            return;
        }

        // Use Avalonia's file picker
        var dialog = new OpenFileDialog
        {
            Title = "Open",
            Filters = new List<FileDialogFilter>
            {
                new() { Name = "Text Documents", Extensions = { "txt" } },
                new() { Name = "All Files", Extensions = { "*" } }
            }
        };

        var result = await dialog.ShowAsync(GetMainWindow());
        if (result?.Length > 0)
        {
            await LoadFileAsync(result[0]);
        }
    }

    public async Task LoadFileAsync(string path)
    {
        try
        {
            var (content, encoding, encodingType, lineEnding) = await _fileService.LoadFileAsync(path);

            FileContent = content; // NeoEditor will pick this up via OneWay binding
            Document = new DocumentModel
            {
                FilePath = path,
                IsUntitled = false,
                IsModified = false,
                Encoding = encoding,
                EncodingType = encodingType,
                SaveEncodingType = encodingType,
                LineEnding = lineEnding
            };

            // Auto-Detect Language
            CurrentLanguage = SyntaxMatcher.DetectLanguage(path);

            _settings.LastEncoding = encodingType;
            _settingsService.SaveEditorSettings(_settings);
            UpdateEncodingDisplay();
            UpdateStatus();
            AddRecentFile(path);
        }
        catch (Exception ex)
        {
            await ShowErrorAsync($"Cannot open file: {ex.Message}");
        }
    }


    /// <summary>
    /// Maps to: Command ID 3, function_14000b874
    /// </summary>
    [RelayCommand]
    private async Task SaveFileAsync()
    {
        if (Document.IsUntitled)
        {
            await SaveFileAsAsync();
            return;
        }

        await SaveToFileAsync(Document.FilePath, Document.SaveEncodingType);
    }

    /// <summary>
    /// Maps to: Command ID 4
    /// </summary>
    [RelayCommand]
    private async Task SaveFileAsAsync()
    {
        var dialog = new SaveFileDialog //todo custom save file dialog
        {
            Title = "Save As",
            DefaultExtension = "txt",
            Filters = new List<FileDialogFilter>
            {
                new() { Name = "Text Documents", Extensions = { "txt" } },
                new() { Name = "All Files", Extensions = { "*" } }
            }
        };

        if (!Document.IsUntitled)
        {
            dialog.InitialFileName = Path.GetFileName(Document.FilePath);
            dialog.Directory = Path.GetDirectoryName(Document.FilePath);
        }

        var result = await dialog.ShowAsync(GetMainWindow());
        if (string.IsNullOrEmpty(result))
        {
            return;
        }

        var encodingDialog = new EncodingDialog(Document.SaveEncodingType);
        var encodingSelection = await encodingDialog.ShowDialog<FileEncodingType?>(GetMainWindow());
        if (encodingSelection.HasValue)
        {
            Document.SaveEncodingType = encodingSelection.Value;
            await SaveToFileAsync(result, encodingSelection.Value);
        }
    }

    private async Task SaveToFileAsync(string path, FileEncodingType encodingType)
    {
        try
        {
            if (EditorControl == null) return;
            string currentContent = EditorControl.GetText(); // Pull from NeoEditor

            await _fileService.SaveFileAsync(path, currentContent, encodingType, Document.LineEnding);

            Document.FilePath = path;
            Document.IsUntitled = false;
            Document.IsModified = false;
            Document.EncodingType = encodingType;
            Document.SaveEncodingType = encodingType;
            Document.Encoding = _fileService.GetEncoding(encodingType);

            _settings.LastEncoding = encodingType;
            _settingsService.SaveEditorSettings(_settings);
            OnPropertyChanged(nameof(Document));
            AddRecentFile(path);
            UpdateEncodingDisplay();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync($"Cannot save file: {ex.Message}");
        }
    }

    // ==================== Edit Commands ====================

    /// <summary>
    /// Maps to: Command ID 768 (EM_UNDO)
    /// </summary>
    [RelayCommand]
    private void Undo() => EditorControl?.Undo();

    [RelayCommand]
    private void Redo() => EditorControl?.Redo();

    /// <summary>
    /// Maps to: Command ID 769 (WM_CUT)
    /// </summary>
    [RelayCommand]
    private void Cut() => EditorControl?.Cut();

    /// <summary>
    /// Maps to: Command ID 770 (WM_COPY)
    /// </summary>
    [RelayCommand]
    private void Copy() => EditorControl?.Copy();

    /// <summary>
    /// Maps to: Command ID 771 (WM_PASTE)
    /// </summary>
    [RelayCommand]
    private void Paste() => EditorControl?.Paste();

    /// <summary>
    /// Maps to: Command ID 20
    /// </summary>
    [RelayCommand]
    private void Delete()
    {
        EditorControl?.DeleteSelection();
    }

    /// <summary>
    /// Maps to: Command ID 25 (EM_SETSEL 0, -1)
    /// </summary>
    [RelayCommand]
    private void SelectAll() => TextEditor?.SelectAll();

    /// <summary>
    /// Maps to: Command ID 26
    /// </summary>
    [RelayCommand]
    private void InsertDateTime()
    {
        var now = DateTime.Now;
        var culture = CultureInfo.CurrentCulture;
        var dateTime = $"{now.ToString("d", culture)} {now.ToString("t", culture)}";

        EditorControl?.InsertAtCaret(dateTime);
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
        var dialog = new PageSetupDialog(PageSetupSettings);
        var result = await dialog.ShowDialog<PageSetupSettings?>(GetMainWindow());
        if (result != null)
        {
            PageSetupSettings = result;
            _settingsService.SavePageSetupSettings(PageSetupSettings);
        }
    }

    [RelayCommand]
    private async Task PrintAsync()
    {
        var pages = _printService.Paginate(
            EditorControl.GetText(),
            Document,
            PageSetupSettings,
            charsPerLine: 80,
            linesPerPage: 50);

        await MessageBoxManager.GetMessageBoxStandard(
            "Notepad",
            $"Prepared {pages.Count} page(s) for printing (platform-specific rendering not yet implemented).",
            ButtonEnum.Ok).ShowAsync();
    }

    [RelayCommand]
    private async Task OnExitAsync()
    {
        //if (await CheckSaveChangesAsync())
        {
            //SaveSessionSettings();
            GetMainWindow().Close();
        }
    } 

    /// <summary>
    /// Maps to: Command ID 28 (F3)
    /// </summary>
    [RelayCommand]
    private async Task FindNext()
    {
        await FindCoreAsync(reverse: false);
    }

    /// <summary>
    /// Maps to: Shift+F3 (reverse find)
    /// </summary>
    [RelayCommand]
    private async Task FindPrevious()
    {
        await FindCoreAsync(reverse: true);
    }


    private async Task FindCoreAsync(bool reverse)
    {
        if (EditorControl == null) return;
        if (string.IsNullOrEmpty(SearchSettings.SearchString)) return;

        var searchUp = reverse ? !SearchSettings.SearchUp : SearchSettings.SearchUp;

        // Updated to use NeoEditor
        var (result, wrapped) = searchUp
            ? _searchService.FindPrevious(EditorControl, SearchSettings)
            : _searchService.FindNext(EditorControl, SearchSettings);

        if (result != null)
        {
            EditorControl.Select(result.StartOffset, result.Length);
            // EditorControl.ScrollToLine(TextEditor.Document.GetLineByOffset(result.StartOffset).LineNumber);
            // EditorControl handles scrolling in Select logic automatically or add ScrollTo here
            return;
        }

        await ShowErrorAsync(wrapped ? "Search wrapped." : $"Cannot find \"{SearchSettings.SearchString}\"");
    }

    [RelayCommand]
    private async Task ReplaceNext()
    {
        if (EditorControl == null || string.IsNullOrEmpty(SearchSettings.SearchString)) return;

        // Find next logic
        var (result, _) = _searchService.FindNext(EditorControl, SearchSettings);
        if (result == null)
        {
            await ShowErrorAsync($"Cannot find \"{SearchSettings.SearchString}\"");
            return;
        }

        // Perform replace on NeoEditor
        EditorControl.ReplaceRange(result.StartOffset, result.Length, SearchSettings.ReplaceString);
        Document.IsModified = true;
        OnPropertyChanged(nameof(Document));
    }

    [RelayCommand]
    private async Task ReplaceAll()
    {
        if (EditorControl == null || string.IsNullOrEmpty(SearchSettings.SearchString)) return;

        int count = _searchService.ReplaceAll(EditorControl, SearchSettings);
        await MessageBoxManager.GetMessageBoxStandard("Notepad", $"Replaced {count} occurrence(s).", ButtonEnum.Ok)
            .ShowAsync();

        if (count > 0)
        {
            Document.IsModified = true;
            OnPropertyChanged(nameof(Document));
        }
    }

    /// <summary>
    /// Maps to: Command ID 24 (Go To dialog)
    /// </summary>
    [RelayCommand]
    private async Task GoToLineAsync()
    {
        if (Settings.WordWrap || EditorControl == null) return;
        var dialog = new GoToLineDialog(EditorControl.LineCount); // Use NeoEditor LineCount
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
        if (_findReplaceDialog != null)
        {
            _findReplaceDialog.DataContext = new FindReplaceViewModel(this, findMode);
            _findReplaceDialog.Title = findMode ? "Find" : "Replace";
            if (!_findReplaceDialog.IsVisible)
            {
                _findReplaceDialog.Show(GetMainWindow());
            }
            else
            {
                _findReplaceDialog.Activate();
            }

            return;
        }

        _findReplaceDialog = new FindReplaceDialog(this, findMode);
        _findReplaceDialog.Closed += (_, _) => _findReplaceDialog = null;
        _findReplaceDialog.Show(GetMainWindow());
    }

    // ==================== Format Commands ====================

    /// <summary>
    /// Maps to: Command ID 32, function_14001d364
    /// </summary>
    [RelayCommand]
    private void ToggleWordWrap()
    {
        Settings.WordWrap = !Settings.WordWrap;

        if (Settings.WordWrap)
        {
            _statusBarBeforeWrap = Settings.ShowStatusBar;
            Settings.ShowStatusBar = false;
        }
        else
        {
            Settings.ShowStatusBar = _statusBarBeforeWrap;
        }

        OnPropertyChanged(nameof(Settings));
        OnPropertyChanged(nameof(CanUseGoToLine));
        OnPropertyChanged(nameof(CanToggleStatusBar));
        _settingsService.SaveEditorSettings(Settings);
        UpdateStatus();
    }

    /// <summary>
    /// Maps to: Command ID 33 (ChooseFontW)
    /// </summary>
    [RelayCommand]
    private async Task ChangeFontAsync()
    {
        // Show font picker dialog
        // (Avalonia doesn't have built-in font dialog, need custom)
        var dialog = new FontDialog
        {
            FontFamily = Settings.FontFamily,
            FontSize = Settings.FontSize,
            IsBold = Settings.IsBold,
            IsItalic = Settings.IsItalic
        };

        var result = await dialog.ShowDialog<bool>(GetMainWindow());
        if (result)
        {
            Settings.FontFamily = dialog.FontFamily;
            Settings.FontSize = dialog.FontSize;
            Settings.IsBold = dialog.IsBold;
            Settings.IsItalic = dialog.IsItalic;
            OnPropertyChanged(nameof(Settings));
            OnPropertyChanged(nameof(EditorFontSize));
            OnPropertyChanged(nameof(EditorFontWeight));
            OnPropertyChanged(nameof(EditorFontStyle));
            _settingsService.SaveEditorSettings(Settings);
        }
    }

    // ==================== View Commands ====================

    /// <summary>
    /// Maps to: Command ID 27
    /// </summary>
    [RelayCommand]
    private void ToggleStatusBar()
    {
        if (Settings.WordWrap)
        {
            return;
        }

        Settings.ShowStatusBar = !Settings.ShowStatusBar;
        OnPropertyChanged(nameof(Settings));
        _settingsService.SaveEditorSettings(Settings);
    }

    /// <summary>
    /// Maps to: Command IDs 34, 35, 36
    /// </summary>
    [RelayCommand]
    private void ZoomIn()
    {
        Settings.ZoomLevel = Math.Min(500, Settings.ZoomLevel + 10);
        OnPropertyChanged(nameof(Settings));
        OnPropertyChanged(nameof(EditorFontSize));
        OnPropertyChanged(nameof(EditorFontWeight));
        OnPropertyChanged(nameof(EditorFontStyle));
        _settingsService.SaveEditorSettings(Settings);
    }

    [RelayCommand]
    private void ZoomOut()
    {
        Settings.ZoomLevel = Math.Max(10, Settings.ZoomLevel - 10);
        OnPropertyChanged(nameof(Settings));
        OnPropertyChanged(nameof(EditorFontSize));
        OnPropertyChanged(nameof(EditorFontWeight));
        OnPropertyChanged(nameof(EditorFontStyle));
        _settingsService.SaveEditorSettings(Settings);
    }

    [RelayCommand]
    private void RestoreZoom()
    {
        Settings.ZoomLevel = 100;
        OnPropertyChanged(nameof(Settings));
        OnPropertyChanged(nameof(EditorFontSize));
        OnPropertyChanged(nameof(EditorFontWeight));
        OnPropertyChanged(nameof(EditorFontStyle));
        _settingsService.SaveEditorSettings(Settings);
    }

    // ==================== Help Commands ====================

    [RelayCommand]
    private void ViewHelp()
    {
        // Open help URL (like original Notepad)
        Process.Start(new ProcessStartInfo
        {
            FileName = "https://go.microsoft.com/fwlink/?LinkId=834783",
            UseShellExecute = true
        });
    }

    [RelayCommand]
    private void ShowAbout()
    {
        var dialog = new AboutDialog();
        dialog.ShowDialog(GetMainWindow());
    }

    // ==================== Helper Methods ====================

    public async Task<bool> CheckSaveChangesAsync()
    {
        if (!Document.IsModified) return true;

        var result = await MessageBoxManager.GetMessageBoxStandard("Notepad",
            $"Do you want to save changes to {Document.FileName}?", ButtonEnum.YesNoCancel).ShowAsync();

        if (result == ButtonResult.Yes)
        {
            await SaveFileAsync();
            return !Document.IsModified;
        }

        return result == ButtonResult.No;
    }

    [RelayCommand]
    public async Task OpenRecentFileAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        if (!File.Exists(path))
        {
            RecentFiles.Remove(path);
            _settingsService.SaveRecentFiles(RecentFiles);
            await ShowErrorAsync("File not found.");
            return;
        }

        if (!await CheckSaveChangesAsync()) return;

        await LoadFileAsync(path);
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
            FileEncodingType.UTF8BOM => "UTF-8",
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
        OnPropertyChanged(nameof(Settings));
        _settingsService.SaveEditorSettings(Settings);
    }

    public void SaveSessionSettings()
    {
        Settings.LastEncoding = Document.SaveEncodingType;
        _settingsService.SaveEditorSettings(Settings);
        _settingsService.SaveSearchSettings(SearchSettings);
        _settingsService.SavePageSetupSettings(PageSetupSettings);
        _settingsService.SaveRecentFiles(RecentFiles);
    }

    public bool CanUseGoToLine => !Settings.WordWrap;

    private void AddRecentFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        var existingIndex = RecentFiles.IndexOf(path);
        if (existingIndex >= 0)
        {
            RecentFiles.RemoveAt(existingIndex);
        }

        RecentFiles.Insert(0, path);
        while (RecentFiles.Count > 10)
        {
            RecentFiles.RemoveAt(RecentFiles.Count - 1);
        }

        _settingsService.SaveRecentFiles(RecentFiles);
    }

    // TextEditor reference (set from View)
    public AvaloniaEdit.TextEditor? TextEditor { get; set; }

    private Window GetMainWindow() =>
        Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow!
            : throw new InvalidOperationException();

    private Task ShowErrorAsync(string message) =>
        MessageBoxManager.GetMessageBoxStandard("Error", message, ButtonEnum.Ok).ShowAsync();
}