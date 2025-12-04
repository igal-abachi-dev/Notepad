using AvaloniaEdit.Document;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NotepadAvalonia.Models;
using NotepadAvalonia.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using NotepadAvalonia.Views;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

namespace NotepadAvalonia.ViewModels;

/// <summary>
/// Main ViewModel - maps to Notepad's global state and command handlers
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    private readonly FileService _fileService;
    private readonly SearchService _searchService;
    private readonly SettingsService _settingsService;

    [ObservableProperty]
    private DocumentModel _document = new();

    [ObservableProperty]
    private EditorSettings _settings = new();

    [ObservableProperty]
    private SearchSettings _searchSettings = new();

    [ObservableProperty]
    private TextDocument _textDocument = new();

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

    public MainWindowViewModel(
        FileService fileService,
        SearchService searchService,
        SettingsService settingsService)
    {
        _fileService = fileService;
        _searchService = searchService;
        _settingsService = settingsService;

        _settings = _settingsService.LoadEditorSettings();
        _searchSettings = _settingsService.LoadSearchSettings();
        _pageSetupSettings = _settingsService.LoadPageSetupSettings();

        TextDocument.TextChanged += OnTextChanged;
        OnPropertyChanged(nameof(EditorFontSize));
        OnPropertyChanged(nameof(EditorFontWeight));
        OnPropertyChanged(nameof(EditorFontStyle));
    }

    public double EditorFontSize => Settings.FontSize * Settings.ZoomLevel / 100.0;
    public FontWeight EditorFontWeight => Settings.IsBold ? FontWeight.Bold : FontWeight.Normal;
    public FontStyle EditorFontStyle => Settings.IsItalic ? FontStyle.Italic : FontStyle.Normal;

    private void OnTextChanged(object? sender, EventArgs e)
    {
        if (!Document.IsModified)
        {
            Document.IsModified = true;
            OnPropertyChanged(nameof(Document));
        }
    }

    partial void OnTextDocumentChanging(TextDocument value)
    {
        TextDocument.TextChanged -= OnTextChanged;
    }

    partial void OnTextDocumentChanged(TextDocument value)
    {
        TextDocument.TextChanged += OnTextChanged;
    }

    // ==================== File Commands ====================

    /// <summary>
    /// Maps to: Command ID 1, function_14000fe24(1)
    /// </summary>
    [RelayCommand]
    private async Task NewFileAsync()
    {
        if (!await CheckSaveChangesAsync()) return;

        TextDocument = new TextDocument();
        Document = new DocumentModel();
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
            var (content, encoding, lineEnding) = await _fileService.LoadFileAsync(path);

            TextDocument = new TextDocument(content);
            Document = new DocumentModel
            {
                FilePath = path,
                IsUntitled = false,
                IsModified = false,
                Encoding = encoding,
                LineEnding = lineEnding
            };

            UpdateEncodingDisplay();
            UpdateStatus();
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

        await SaveToFileAsync(Document.FilePath);
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
        if (!string.IsNullOrEmpty(result))
        {
            await SaveToFileAsync(result);
        }
    }

    private async Task SaveToFileAsync(string path)
    {
        try
        {
            await _fileService.SaveFileAsync(
                path,
                TextDocument.Text,
                Document.Encoding,
                Document.LineEnding);

            Document.FilePath = path;
            Document.IsUntitled = false;
            Document.IsModified = false;
            OnPropertyChanged(nameof(Document));
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
    private void Undo() => TextEditor?.Undo();

    [RelayCommand]
    private void Redo() => TextEditor?.Redo();

    /// <summary>
    /// Maps to: Command ID 769 (WM_CUT)
    /// </summary>
    [RelayCommand]
    private void Cut() => TextEditor?.Cut();

    /// <summary>
    /// Maps to: Command ID 770 (WM_COPY)
    /// </summary>
    [RelayCommand]
    private void Copy() => TextEditor?.Copy();

    /// <summary>
    /// Maps to: Command ID 771 (WM_PASTE)
    /// </summary>
    [RelayCommand]
    private void Paste() => TextEditor?.Paste();

    /// <summary>
    /// Maps to: Command ID 20
    /// </summary>
    [RelayCommand]
    private void Delete()
    {
        if (TextEditor?.SelectionLength > 0)
        {
            TextEditor.SelectedText = "";
        }
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
        var dateTime = DateTime.Now.ToString("h:mm tt M/d/yyyy");
        TextEditor?.Document.Insert(TextEditor.CaretOffset, dateTime);
    }

    // ==================== Find/Replace ====================

    /// <summary>
    /// Maps to: Command ID 21, function_14001cfac
    /// </summary>
    [RelayCommand]
    private void ShowFindDialog()
    {
        // Open find dialog (modeless)
        var dialog = new FindReplaceDialog(this, findMode: true);
        dialog.Show(GetMainWindow());
    }

    /// <summary>
    /// Maps to: Command ID 23
    /// </summary>
    [RelayCommand]
    private void ShowReplaceDialog()
    {
        var dialog = new FindReplaceDialog(this, findMode: false);
        dialog.Show(GetMainWindow());
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
        await MessageBoxManager.GetMessageBoxStandard(
            "Notepad",
            "Printing is not implemented yet.",
            ButtonEnum.Ok).ShowAsync();
    }

    [RelayCommand]
    private async Task OnExitAsync()
    {
        if (await CheckSaveChangesAsync())
        {
            SaveSessionSettings();
            GetMainWindow().Close();
        }
    }

    /// <summary>
    /// Maps to: Command ID 28 (F3)
    /// </summary>
    [RelayCommand]
    private void FindNext()
    {
        if (TextEditor == null) return;
        if (string.IsNullOrEmpty(SearchSettings.SearchString))
        {
            StatusText = "Find what?";
            return;
        }

        var result = SearchSettings.SearchUp
            ? _searchService.FindPrevious(TextEditor, SearchSettings)
            : _searchService.FindNext(TextEditor, SearchSettings);

        if (result != null)
        {
            TextEditor.Select(result.StartOffset, result.Length);
            TextEditor.ScrollToLine(TextEditor.Document.GetLineByOffset(result.StartOffset).LineNumber);
        }
        else
        {
            StatusText = $"Cannot find \"{SearchSettings.SearchString}\"";
        }
    }

    [RelayCommand]
    private void ReplaceNext()
    {
        if (TextEditor == null) return;
        if (string.IsNullOrEmpty(SearchSettings.SearchString))
        {
            StatusText = "Find what?";
            return;
        }

        var replaced = _searchService.ReplaceNext(TextEditor, SearchSettings);
        if (replaced == 0)
        {
            StatusText = $"Cannot find \"{SearchSettings.SearchString}\"";
            return;
        }

        Document.IsModified = true;
        OnPropertyChanged(nameof(Document));
    }

    [RelayCommand]
    private void ReplaceAll()
    {
        if (TextEditor == null) return;
        if (string.IsNullOrEmpty(SearchSettings.SearchString))
        {
            StatusText = "Find what?";
            return;
        }

        var count = _searchService.ReplaceAll(TextEditor, SearchSettings);
        if (count == 0)
        {
            StatusText = $"Cannot find \"{SearchSettings.SearchString}\"";
            return;
        }

        StatusText = $"Replaced {count} occurrence{(count == 1 ? "" : "s")}";
        Document.IsModified = true;
        OnPropertyChanged(nameof(Document));
    }

    /// <summary>
    /// Maps to: Command ID 24 (Go To dialog)
    /// </summary>
    [RelayCommand]
    private async Task GoToLineAsync()
    {
        var dialog = new GoToLineDialog(TextDocument.LineCount);

        var result = await dialog.ShowDialog<int?>(GetMainWindow());
        if (result.HasValue && TextEditor != null)
        {
            var line = TextDocument.GetLineByNumber(result.Value);
            TextEditor.CaretOffset = line.Offset;
            TextEditor.ScrollToLine(result.Value);
        }
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
        _settingsService.SaveEditorSettings(Settings);
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
        _settingsService.SaveEditorSettings(Settings);
    }

    [RelayCommand]
    private void ZoomOut()
    {
        Settings.ZoomLevel = Math.Max(10, Settings.ZoomLevel - 10);
        OnPropertyChanged(nameof(Settings));
        OnPropertyChanged(nameof(EditorFontSize));
        _settingsService.SaveEditorSettings(Settings);
    }

    [RelayCommand]
    private void RestoreZoom()
    {
        Settings.ZoomLevel = 100;
        OnPropertyChanged(nameof(Settings));
        OnPropertyChanged(nameof(EditorFontSize));
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

    public void UpdateCaretPosition(int line, int column)
    {
        CaretLine = line;
        CaretColumn = column;
        StatusText = $"Ln {line}, Col {column}";
    }

    private void UpdateEncodingDisplay()
    {
        EncodingText = Document.Encoding.WebName.ToUpperInvariant();
        LineEndingText = Document.LineEnding switch
        {
            LineEndingStyle.CR => "CR",
            LineEndingStyle.LF => "LF",
            _ => "CRLF"
        };
    }

    private void UpdateStatus()
    {
        StatusText = "Ready";
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
        _settingsService.SaveEditorSettings(Settings);
        _settingsService.SaveSearchSettings(SearchSettings);
        _settingsService.SavePageSetupSettings(PageSetupSettings);
    }

    // TextEditor reference (set from View)
    public AvaloniaEdit.TextEditor? TextEditor { get; set; }

    private Window GetMainWindow() =>
        Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow!
            : throw new InvalidOperationException();

    private Task ShowErrorAsync(string message) =>
        MessageBoxManager.GetMessageBoxStandard("Error",message, ButtonEnum.Ok).ShowAsync();
}
