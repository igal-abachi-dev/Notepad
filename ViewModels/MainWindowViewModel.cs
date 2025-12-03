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
using System.Reflection.Metadata;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
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
    private TextDocument _textDocument = new("hello world".ToCharArray());

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

        _textDocument.TextChanged += OnTextChanged;
    }

    private void OnTextChanged(object? sender, EventArgs e)
    {
        if (!_document.IsModified)
        {
            _document.IsModified = true;
            OnPropertyChanged(nameof(Document));
        }
    }

    // ==================== File Commands ====================

    /// <summary>
    /// Maps to: Command ID 1, function_14000fe24(1)
    /// </summary>
    [RelayCommand]
    private async Task NewFileAsync()
    {
        if (!await CheckSaveChangesAsync()) return;

        _textDocument = new TextDocument();
        _document = new DocumentModel();
        UpdateStatus();
    }

    /// <summary>
    /// Maps to: Command ID 2, function_140008c18
    /// </summary>
    [RelayCommand]
    public async Task OpenFileAsync()
    {
        if (!await CheckSaveChangesAsync()) return;

        // Use Avalonia's file picker
        var dialog = new OpenFileDialog //todo custom open file dialog
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

            _textDocument = new TextDocument(content);
            _document = new DocumentModel
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
        if (_document.IsUntitled)
        {
            await SaveFileAsAsync();
            return;
        }

        await SaveToFileAsync(_document.FilePath);
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

        if (!_document.IsUntitled)
        {
            dialog.InitialFileName = Path.GetFileName(_document.FilePath);
            dialog.Directory = Path.GetDirectoryName(_document.FilePath);
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
                _textDocument.Text,
                _document.Encoding,
                _document.LineEnding);

            _document.FilePath = path;
            _document.IsUntitled = false;
            _document.IsModified = false;
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
        dialog.Show();
    }

    /// <summary>
    /// Maps to: Command ID 23
    /// </summary>
    [RelayCommand]
    private void ShowReplaceDialog()
    {
        var dialog = new FindReplaceDialog(this, findMode: false);
        dialog.Show();
    }

    [RelayCommand]
    private void ShowPageSetupDialog() //todo: add dialog xaml
    {
        //var dialog = new PageSetupDialog(this);
        //dialog.Show();
    }
    [RelayCommand]
    private void Print() 
    {
    }

    [RelayCommand]
    private void OnExit()
    {
    }

    /// <summary>
    /// Maps to: Command ID 28 (F3)
    /// </summary>
    [RelayCommand]
    private void FindNext()
    {
        if (TextEditor == null) return;

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

    /// <summary>
    /// Maps to: Command ID 24 (Go To dialog)
    /// </summary>
    [RelayCommand]
    private async Task GoToLineAsync()
    {
        var dialog = new GoToLineDialog
        {
            MaxLine = TextDocument.LineCount
        };

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
            FontSize = Settings.FontSize
        };

        var result = await dialog.ShowDialog<bool>(GetMainWindow());
        if (result)
        {
            Settings.FontFamily = dialog.FontFamily.Name;
            Settings.FontSize = dialog.FontSize;
            OnPropertyChanged(nameof(Settings));
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
    }

    [RelayCommand]
    private void ZoomOut()
    {
        Settings.ZoomLevel = Math.Max(10, Settings.ZoomLevel - 10);
        OnPropertyChanged(nameof(Settings));
    }

    [RelayCommand]
    private void RestoreZoom()
    {
        Settings.ZoomLevel = 100;
        OnPropertyChanged(nameof(Settings));
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
        LineEndingText = Document.LineEnding.ToString();
    }

    private void UpdateStatus()
    {
        StatusText = "Ready";
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