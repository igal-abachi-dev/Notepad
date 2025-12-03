using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using AvaloniaNotePad.Models;
using AvaloniaNotePad.Services;

namespace AvaloniaNotePad.ViewModels;

/// <summary>
/// Main ViewModel containing all Notepad application logic
/// </summary>
public class MainWindowViewModel : INotifyPropertyChanged
//    public partial class MainWindowViewModel : ViewModelBase
{
    private readonly FileService _fileService;
    private readonly FindReplaceService _findReplaceService;
    private readonly SettingsService _settingsService;
    
    private TabViewModel? _selectedTab;
    private bool _showFindReplace;
    private bool _isReplaceMode;
    private string _findText = string.Empty;
    private string _replaceText = string.Empty;
    private string _statusMessage = string.Empty;
    private bool _wordWrap;
    private bool _showLineNumbers = true;
    private bool _showStatusBar = true;
    private AppTheme _theme = AppTheme.System;

    // Commands (initialized by the view)
    public ICommand NewDocumentCommand { get; set; } = null!;
    public ICommand NewWindowCommand { get; set; } = null!;
    public ICommand OpenCommand { get; set; } = null!;
    public ICommand SaveCommand { get; set; } = null!;
    public ICommand SaveAsCommand { get; set; } = null!;
    public ICommand CloseTabCommand { get; set; } = null!;
    public ICommand ExitCommand { get; set; } = null!;
    public ICommand UndoCommand { get; set; } = null!;
    public ICommand RedoCommand { get; set; } = null!;
    public ICommand CutCommand { get; set; } = null!;
    public ICommand CopyCommand { get; set; } = null!;
    public ICommand PasteCommand { get; set; } = null!;
    public ICommand DeleteCommand { get; set; } = null!;
    public ICommand SelectAllCommand { get; set; } = null!;
    public ICommand InsertDateTimeCommand { get; set; } = null!;
    public ICommand FindCommand { get; set; } = null!;
    public ICommand ReplaceCommand { get; set; } = null!;
    public ICommand FindNextCommand { get; set; } = null!;
    public ICommand FindPreviousCommand { get; set; } = null!;
    public ICommand ReplaceCurrentCommand { get; set; } = null!;
    public ICommand ReplaceAllCommand { get; set; } = null!;
    public ICommand HideFindReplaceCommand { get; set; } = null!;
    public ICommand GoToLineCommand { get; set; } = null!;
    public ICommand ZoomInCommand { get; set; } = null!;
    public ICommand ZoomOutCommand { get; set; } = null!;
    public ICommand ResetZoomCommand { get; set; } = null!;
    public ICommand ToggleWordWrapCommand { get; set; } = null!;
    public ICommand ToggleLineNumbersCommand { get; set; } = null!;
    public ICommand ToggleStatusBarCommand { get; set; } = null!;
    public ICommand FontCommand { get; set; } = null!;
    public ICommand SetEncodingCommand { get; set; } = null!;
    public ICommand SetLineEndingCommand { get; set; } = null!;
    public ICommand AboutCommand { get; set; } = null!;
    public ICommand PrintCommand { get; set; } = null!;
    public ICommand PageSetupCommand { get; set; } = null!;

    public MainWindowViewModel()
    {
        _fileService = new FileService();
        _findReplaceService = new FindReplaceService();
        _settingsService = new SettingsService();
        
        Tabs = new ObservableCollection<TabViewModel>();
        
        // Initialize with settings
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        await _settingsService.LoadAsync();
        ApplySettings();
        
        // Start with a new document if no tabs
        if (Tabs.Count == 0)
            NewDocument();
    }

    private void ApplySettings()
    {
        var settings = _settingsService.Settings;
        WordWrap = settings.WordWrap;
        ShowLineNumbers = settings.ShowLineNumbers;
        ShowStatusBar = settings.ShowStatusBar;
        Theme = settings.Theme;
    }

    // === Collections ===
    public ObservableCollection<TabViewModel> Tabs { get; }

    public TabViewModel? SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (_selectedTab != value)
            {
                _selectedTab = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasOpenDocument));
                OnPropertyChanged(nameof(CanUndo));
                OnPropertyChanged(nameof(CanRedo));
            }
        }
    }

    public bool HasOpenDocument => SelectedTab != null;

    // === File Operations ===
    
    public void NewDocument()
    {
        var tab = new TabViewModel(new Document());
        Tabs.Add(tab);
        SelectedTab = tab;
    }

    public async Task OpenFileAsync(string filePath)
    {
        try
        {
            // Check if already open
            var existingTab = Tabs.FirstOrDefault(t => 
                t.Document.FilePath?.Equals(filePath, StringComparison.OrdinalIgnoreCase) == true);
            if (existingTab != null)
            {
                SelectedTab = existingTab;
                return;
            }

            var document = await _fileService.OpenFileAsync(filePath);
            var tab = new TabViewModel(document);
            Tabs.Add(tab);
            SelectedTab = tab;
            
            _settingsService.Settings.AddRecentFile(filePath);
            await _settingsService.SaveAsync();
            
            StatusMessage = $"Opened {document.FileName}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error opening file: {ex.Message}";
            throw;
        }
    }

    public async Task<bool> SaveAsync()
    {
        if (SelectedTab == null) return false;

        if (SelectedTab.Document.IsNewDocument)
            return false; // Needs SaveAs dialog
            
        try
        {
            await _fileService.SaveFileAsync(SelectedTab.Document);
            SelectedTab.IsModified = false;
            StatusMessage = $"Saved {SelectedTab.Document.FileName}";
            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error saving: {ex.Message}";
            throw;
        }
    }

    public async Task<bool> SaveAsAsync(string filePath)
    {
        if (SelectedTab == null) return false;
        
        try
        {
            await _fileService.SaveFileAsync(SelectedTab.Document, filePath);
            SelectedTab.IsModified = false;
            
            _settingsService.Settings.AddRecentFile(filePath);
            await _settingsService.SaveAsync();
            
            StatusMessage = $"Saved as {SelectedTab.Document.FileName}";
            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error saving: {ex.Message}";
            throw;
        }
    }

    public bool CloseTab(TabViewModel tab)
    {
        if (tab.IsModified)
            return false; // Caller should prompt to save

        var index = Tabs.IndexOf(tab);
        Tabs.Remove(tab);
        
        if (Tabs.Count > 0)
        {
            SelectedTab = Tabs[Math.Min(index, Tabs.Count - 1)];
        }
        else
        {
            SelectedTab = null;
        }
        
        return true;
    }

    public async Task<bool> CloseAllAsync()
    {
        foreach (var tab in Tabs.ToList())
        {
            if (tab.IsModified)
                return false; // Has unsaved changes
        }
        
        Tabs.Clear();
        SelectedTab = null;
        return true;
    }

    // === Edit Operations ===
    
    // These are typically handled by AvaloniaEdit directly
    public bool CanUndo => false; // Placeholder - handled by TextEditor
    public bool CanRedo => false;

    public void Cut()
    {
        // Handled by TextEditor control
    }

    public void Copy()
    {
        // Handled by TextEditor control
    }

    public void Paste()
    {
        // Handled by TextEditor control
    }

    public void Delete()
    {
        if (SelectedTab?.HasSelection == true)
        {
            SelectedTab.ReplaceSelection(string.Empty);
        }
    }

    public void SelectAll()
    {
        SelectedTab?.SelectAll();
    }

    public void InsertDateTime()
    {
        SelectedTab?.InsertDateTime();
    }

    // === Find/Replace ===
    
    public bool ShowFindReplace
    {
        get => _showFindReplace;
        set
        {
            if (_showFindReplace != value)
            {
                _showFindReplace = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsReplaceMode
    {
        get => _isReplaceMode;
        set
        {
            if (_isReplaceMode != value)
            {
                _isReplaceMode = value;
                OnPropertyChanged();
            }
        }
    }

    public string FindText
    {
        get => _findText;
        set
        {
            if (_findText != value)
            {
                _findText = value;
                OnPropertyChanged();
            }
        }
    }

    public string ReplaceText
    {
        get => _replaceText;
        set
        {
            if (_replaceText != value)
            {
                _replaceText = value;
                OnPropertyChanged();
            }
        }
    }

    // Search options from settings
    public bool MatchCase
    {
        get => _settingsService.Settings.MatchCase;
        set
        {
            _settingsService.Settings.MatchCase = value;
            OnPropertyChanged();
        }
    }

    public bool MatchWholeWord
    {
        get => _settingsService.Settings.MatchWholeWord;
        set
        {
            _settingsService.Settings.MatchWholeWord = value;
            OnPropertyChanged();
        }
    }

    public bool UseRegex
    {
        get => _settingsService.Settings.UseRegex;
        set
        {
            _settingsService.Settings.UseRegex = value;
            OnPropertyChanged();
        }
    }

    public void ShowFind()
    {
        IsReplaceMode = false;
        ShowFindReplace = true;
        
        // Use selected text as search term
        var selected = SelectedTab?.GetSelectedText();
        if (!string.IsNullOrEmpty(selected) && !selected.Contains('\n'))
            FindText = selected;
    }

    public void ShowReplace()
    {
        IsReplaceMode = true;
        ShowFindReplace = true;
        
        var selected = SelectedTab?.GetSelectedText();
        if (!string.IsNullOrEmpty(selected) && !selected.Contains('\n'))
            FindText = selected;
    }

    public void HideFindReplace()
    {
        ShowFindReplace = false;
    }

    public FindResult? FindNext()
    {
        if (SelectedTab == null || string.IsNullOrEmpty(FindText))
            return null;

        var options = new FindOptions
        {
            MatchCase = MatchCase,
            MatchWholeWord = MatchWholeWord,
            UseRegex = UseRegex,
            WrapAround = true
        };

        var startIndex = SelectedTab.CaretOffset;
        if (SelectedTab.SelectionLength > 0)
            startIndex = SelectedTab.SelectionStart + SelectedTab.SelectionLength;

        var result = _findReplaceService.FindNext(
            SelectedTab.Text, FindText, startIndex, options);

        if (result != null)
        {
            SelectedTab.SelectionStart = result.Index;
            SelectedTab.SelectionLength = result.Length;
            SelectedTab.CaretOffset = result.Index + result.Length;
            StatusMessage = string.Empty;
        }
        else
        {
            StatusMessage = $"Cannot find \"{FindText}\"";
        }

        _settingsService.Settings.AddFindHistory(FindText);
        return result;
    }

    public FindResult? FindPrevious()
    {
        if (SelectedTab == null || string.IsNullOrEmpty(FindText))
            return null;

        var options = new FindOptions
        {
            MatchCase = MatchCase,
            MatchWholeWord = MatchWholeWord,
            UseRegex = UseRegex,
            WrapAround = true
        };

        var result = _findReplaceService.FindPrevious(
            SelectedTab.Text, FindText, SelectedTab.SelectionStart, options);

        if (result != null)
        {
            SelectedTab.SelectionStart = result.Index;
            SelectedTab.SelectionLength = result.Length;
            SelectedTab.CaretOffset = result.Index;
            StatusMessage = string.Empty;
        }
        else
        {
            StatusMessage = $"Cannot find \"{FindText}\"";
        }

        return result;
    }

    public void ReplaceCurrent()
    {
        if (SelectedTab == null || string.IsNullOrEmpty(FindText))
            return;

        var options = new FindOptions
        {
            MatchCase = MatchCase,
            MatchWholeWord = MatchWholeWord,
            UseRegex = UseRegex,
            WrapAround = true
        };

        var (newText, nextResult) = _findReplaceService.ReplaceNext(
            SelectedTab.Text, FindText, ReplaceText,
            SelectedTab.SelectionStart, SelectedTab.SelectionLength, options);

        SelectedTab.Text = newText;
        
        if (nextResult != null)
        {
            SelectedTab.SelectionStart = nextResult.Index;
            SelectedTab.SelectionLength = nextResult.Length;
            SelectedTab.CaretOffset = nextResult.Index + nextResult.Length;
        }

        _settingsService.Settings.AddReplaceHistory(ReplaceText);
    }

    public void ReplaceAll()
    {
        if (SelectedTab == null || string.IsNullOrEmpty(FindText))
            return;

        var options = new FindOptions
        {
            MatchCase = MatchCase,
            MatchWholeWord = MatchWholeWord,
            UseRegex = UseRegex
        };

        var (newText, count) = _findReplaceService.ReplaceAll(
            SelectedTab.Text, FindText, ReplaceText, options);

        SelectedTab.Text = newText;
        StatusMessage = $"Replaced {count} occurrence(s)";

        _settingsService.Settings.AddReplaceHistory(ReplaceText);
    }

    // === Go To ===
    
    public void GoToLine(int lineNumber)
    {
        SelectedTab?.GoToLine(lineNumber);
    }

    // === View Settings ===
    
    public bool WordWrap
    {
        get => _wordWrap;
        set
        {
            if (_wordWrap != value)
            {
                _wordWrap = value;
                _settingsService.Settings.WordWrap = value;
                OnPropertyChanged();
            }
        }
    }

    public bool ShowLineNumbers
    {
        get => _showLineNumbers;
        set
        {
            if (_showLineNumbers != value)
            {
                _showLineNumbers = value;
                _settingsService.Settings.ShowLineNumbers = value;
                OnPropertyChanged();
            }
        }
    }

    public bool ShowStatusBar
    {
        get => _showStatusBar;
        set
        {
            if (_showStatusBar != value)
            {
                _showStatusBar = value;
                _settingsService.Settings.ShowStatusBar = value;
                OnPropertyChanged();
            }
        }
    }

    public AppTheme Theme
    {
        get => _theme;
        set
        {
            if (_theme != value)
            {
                _theme = value;
                _settingsService.Settings.Theme = value;
                OnPropertyChanged();
            }
        }
    }

    // === Zoom ===
    
    public void ZoomIn()
    {
        if (SelectedTab != null)
            SelectedTab.ZoomLevel = Math.Min(SelectedTab.ZoomLevel + 10, 500);
    }

    public void ZoomOut()
    {
        if (SelectedTab != null)
            SelectedTab.ZoomLevel = Math.Max(SelectedTab.ZoomLevel - 10, 10);
    }

    public void ResetZoom()
    {
        if (SelectedTab != null)
            SelectedTab.ZoomLevel = 100;
    }

    // === Status Bar ===
    
    public string StatusMessage
    {
        get => _statusMessage;
        set
        {
            if (_statusMessage != value)
            {
                _statusMessage = value;
                OnPropertyChanged();
            }
        }
    }

    // === Settings Persistence ===
    
    public async Task SaveSettingsAsync()
    {
        await _settingsService.SaveAsync();
    }

    public AppSettings Settings => _settingsService.Settings;

    // === Encoding/Line Ending Changes ===
    
    public void SetEncoding(System.Text.Encoding encoding)
    {
        if (SelectedTab != null)
        {
            SelectedTab.Document.Encoding = encoding;
            SelectedTab.Document.IsModified = true;
            OnPropertyChanged(nameof(SelectedTab));
        }
    }

    public void SetLineEnding(LineEnding lineEnding)
    {
        if (SelectedTab != null)
        {
            SelectedTab.Document.LineEnding = lineEnding;
            SelectedTab.Document.IsModified = true;
            OnPropertyChanged(nameof(SelectedTab));
        }
    }

    // === INotifyPropertyChanged ===
    
    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
