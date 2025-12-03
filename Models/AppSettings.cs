using System.Collections.Generic;
using System.Text.Json;
using Avalonia.Media;

namespace AvaloniaNotePad.Models;

/// <summary>
/// Application settings matching Windows 11 Notepad features
/// </summary>
public class AppSettings
{
    // Editor settings
    public string FontFamily { get; set; } = "Cascadia Code";
    public double FontSize { get; set; } = 14;
    public bool WordWrap { get; set; } = false;
    public bool ShowStatusBar { get; set; } = true;
    public bool ShowLineNumbers { get; set; } = true;
    
    // Theme
    public AppTheme Theme { get; set; } = AppTheme.System;
    
    // Tab settings
    public int TabSize { get; set; } = 4;
    public bool UseSpacesForTab { get; set; } = false;
    
    // File settings
    public string DefaultEncoding { get; set; } = "UTF-8";
    public string DefaultLineEnding { get; set; } = "CRLF";
    public bool AutoSave { get; set; } = false;
    public int AutoSaveIntervalSeconds { get; set; } = 30;
    
    // Window state
    public double WindowWidth { get; set; } = 800;
    public double WindowHeight { get; set; } = 600;
    public double WindowX { get; set; } = -1;
    public double WindowY { get; set; } = -1;
    public bool IsMaximized { get; set; } = false;
    
    // Recent files
    public List<string> RecentFiles { get; set; } = new();
    public int MaxRecentFiles { get; set; } = 10;
    
    // Find/Replace history
    public List<string> FindHistory { get; set; } = new();
    public List<string> ReplaceHistory { get; set; } = new();
    public int MaxHistoryItems { get; set; } = 20;
    
    // Search options (persist between sessions like real Notepad)
    public bool MatchCase { get; set; } = false;
    public bool MatchWholeWord { get; set; } = false;
    public bool UseRegex { get; set; } = false;
    public SearchDirection SearchDirection { get; set; } = SearchDirection.Down;
    
    public void AddRecentFile(string filePath)
    {
        RecentFiles.Remove(filePath);
        RecentFiles.Insert(0, filePath);
        if (RecentFiles.Count > MaxRecentFiles)
            RecentFiles.RemoveAt(RecentFiles.Count - 1);
    }
    
    public void AddFindHistory(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        FindHistory.Remove(text);
        FindHistory.Insert(0, text);
        if (FindHistory.Count > MaxHistoryItems)
            FindHistory.RemoveAt(FindHistory.Count - 1);
    }
    
    public void AddReplaceHistory(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        ReplaceHistory.Remove(text);
        ReplaceHistory.Insert(0, text);
        if (ReplaceHistory.Count > MaxHistoryItems)
            ReplaceHistory.RemoveAt(ReplaceHistory.Count - 1);
    }
}

public enum AppTheme
{
    Light,
    Dark,
    System
}

public enum SearchDirection
{
    Up,
    Down
}
