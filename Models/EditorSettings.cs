using CommunityToolkit.Mvvm.ComponentModel;

namespace NotepadAvalonia.Models;

public class EditorSettings : ObservableObject
{
    private bool _wordWrap;
    private bool _showStatusBar = true;
    private bool _showLineNumbers = true;
    private bool _windowsOnlyLineEndings;
    private bool _preservePastedLineEndings;
    private bool _rightToLeft;
    private string _fontFamily = "Consolas";
    private int _zoomLevel = 100;

    public bool WordWrap
    {
        get => _wordWrap;
        set => SetProperty(ref _wordWrap, value);
    }

    public bool ShowStatusBar
    {
        get => _showStatusBar;
        set => SetProperty(ref _showStatusBar, value);
    }

    public bool ShowLineNumbers
    {
        get => _showLineNumbers;
        set => SetProperty(ref _showLineNumbers, value);
    }

    public bool WindowsOnlyLineEndings
    {
        get => _windowsOnlyLineEndings;
        set => SetProperty(ref _windowsOnlyLineEndings, value);
    }

    public bool PreservePastedLineEndings
    {
        get => _preservePastedLineEndings;
        set => SetProperty(ref _preservePastedLineEndings, value);
    }

    public bool RightToLeft
    {
        get => _rightToLeft;
        set => SetProperty(ref _rightToLeft, value);
    }

    public string FontFamily
    {
        get => _fontFamily;
        set => SetProperty(ref _fontFamily, value);
    }

    public int ZoomLevel
    {
        get => _zoomLevel;
        set => SetProperty(ref _zoomLevel, value);
    }

    public double FontSize { get; set; } = 11;
    public bool ShowUnicodeControls { get; set; }
    public bool IsBold { get; set; }
    public bool IsItalic { get; set; }
    public int WindowX { get; set; } = 100;
    public int WindowY { get; set; } = 100;
    public int WindowWidth { get; set; } = 800;
    public int WindowHeight { get; set; } = 600;
    public FileEncodingType DefaultEncoding { get; set; } = FileEncodingType.UTF8;
    public EditorTheme Theme { get; set; }

    // Retained for older settings files. New documents use DefaultEncoding.
    public FileEncodingType LastEncoding { get; set; } = FileEncodingType.UTF8;
}

public enum EditorTheme { System, Light, Dark }
