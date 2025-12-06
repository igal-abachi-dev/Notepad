namespace NotepadAvalonia.Models;

/// <summary>
/// Maps to registry settings: fWrap, StatusBar, font settings
/// </summary>
public class EditorSettings
{
    // Word Wrap (maps to g405)
    public bool WordWrap { get; set; } = false;

    // Status Bar (maps to g408)
    public bool ShowStatusBar { get; set; } = true;

    // Font settings (maps to g318-g331, g149)
    public string FontFamily { get; set; } = "Consolas";
    public double FontSize { get; set; } = 11;
    public bool IsBold { get; set; } = false;
    public bool IsItalic { get; set; } = false;

    // Zoom (100 = 100%)
    public int ZoomLevel { get; set; } = 100;

    // Window position (maps to iWindowPosX/Y/DX/DY)
    public int WindowX { get; set; } = 100;
    public int WindowY { get; set; } = 100;
    public int WindowWidth { get; set; } = 800;
    public int WindowHeight { get; set; } = 600;

    // Last-used encoding for Save/Save As (maps to g310)
    public FileEncodingType LastEncoding { get; set; } = FileEncodingType.ANSI;
}
