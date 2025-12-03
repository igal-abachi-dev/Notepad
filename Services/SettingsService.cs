using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using AvaloniaNotePad.Models;

namespace AvaloniaNotePad.Services;

/// <summary>
/// Service to persist and load settings.
/// </summary>
public class SettingsService
{
    private readonly string _settingsPath;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public AppSettings Settings { get; private set; } = new();

    public SettingsService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var appFolder = Path.Combine(appData, "AvaloniaNotePad");
        Directory.CreateDirectory(appFolder);
        _settingsPath = Path.Combine(appFolder, "settings.json");
    }

    public async Task LoadAsync()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = await File.ReadAllTextAsync(_settingsPath);
                Settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
        }
        catch
        {
            Settings = new AppSettings();
        }
    }

    public async Task SaveAsync()
    {
        try
        {
            var json = JsonSerializer.Serialize(Settings, JsonOptions);
            await File.WriteAllTextAsync(_settingsPath, json);
        }
        catch
        {
            // Silently fail - don't crash the app for settings
        }
    }
}
