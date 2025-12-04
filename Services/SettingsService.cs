
using NotepadAvalonia.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;


/// <summary>
/// Cross-platform settings storage
/// Maps to: function_140010bd8 (load), function_1400107bc (save)
/// Uses JSON instead of registry for portability
/// </summary>
public class SettingsService
{
    private readonly string _settingsPath;

    public SettingsService(string? settingsPath = null)
    {
        if (!string.IsNullOrWhiteSpace(settingsPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
            _settingsPath = settingsPath;
            return;
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var settingsDir = Path.Combine(appData, "NotepadAvalonia");
        Directory.CreateDirectory(settingsDir);
        _settingsPath = Path.Combine(settingsDir, "settings.json");
    }

    private AppSettings LoadAll()
    {
        if (!File.Exists(_settingsPath))
            return new AppSettings();

        var json = File.ReadAllText(_settingsPath);
        return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
    }

    private void SaveAll(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(_settingsPath, json);
    }

    public EditorSettings LoadEditorSettings() => LoadAll().Editor;
    public void SaveEditorSettings(EditorSettings settings)
    {
        var all = LoadAll();
        all.Editor = settings;
        SaveAll(all);
    }

    public SearchSettings LoadSearchSettings() => LoadAll().Search;
    public void SaveSearchSettings(SearchSettings settings)
    {
        var all = LoadAll();
        all.Search = settings;
        SaveAll(all);
    }

    public PageSetupSettings LoadPageSetupSettings() => LoadAll().PageSetup;
    public void SavePageSetupSettings(PageSetupSettings settings)
    {
        var all = LoadAll();
        all.PageSetup = settings;
        SaveAll(all);
    }

    public List<string> LoadRecentFiles() => LoadAll().RecentFiles ?? new List<string>();
    public void SaveRecentFiles(IEnumerable<string> files)
    {
        var all = LoadAll();
        all.RecentFiles = new List<string>(files);
        SaveAll(all);
    }

    private class AppSettings
    {
        public EditorSettings Editor { get; set; } = new();
        public SearchSettings Search { get; set; } = new();
        public PageSetupSettings PageSetup { get; set; } = new();
        public List<string> RecentFiles { get; set; } = new();
    }
}
