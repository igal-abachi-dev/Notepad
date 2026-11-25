using NotepadAvalonia.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NotepadAvalonia.Services;

[JsonSerializable(typeof(AppSettings))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal partial class AppSettingsContext : JsonSerializerContext;

public class SettingsService
{
    private static readonly object WriteLock = new();
    private readonly string _settingsPath;
    private ObservableCollection<string>? _sharedRecents;

    public SettingsService(string? settingsPath = null)
    {
        _settingsPath = Path.GetFullPath(string.IsNullOrWhiteSpace(settingsPath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NotepadAvalonia", "settings.json")
            : settingsPath);
    }

    public AppSettings Load()
    {
        try
        {
            using var stream = new FileStream(_settingsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var settings = JsonSerializer.Deserialize(stream, AppSettingsContext.Default.AppSettings) ?? new AppSettings();
            settings.Editor ??= new();
            settings.Search ??= new();
            settings.PageSetup ??= new();
            settings.RecentFiles ??= new();
            settings.RecentFiles.RemoveAll(string.IsNullOrWhiteSpace);
            settings.Search.SearchString ??= "";
            settings.Search.ReplaceString ??= "";
            if (string.IsNullOrWhiteSpace(settings.Editor.FontFamily)) settings.Editor.FontFamily = "Consolas";
            settings.Editor.FontSize = double.IsFinite(settings.Editor.FontSize) ? Math.Clamp(settings.Editor.FontSize, 6, 144) : 11;
            settings.Editor.ZoomLevel = Math.Clamp(settings.Editor.ZoomLevel, 10, 500);
            if (!Enum.IsDefined(settings.Editor.DefaultEncoding)) settings.Editor.DefaultEncoding = FileEncodingType.UTF8;
            settings.Editor.WindowWidth = Math.Clamp(settings.Editor.WindowWidth, 320, 16384);
            settings.Editor.WindowHeight = Math.Clamp(settings.Editor.WindowHeight, 220, 16384);
            return settings;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return new();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning("Could not load the Notepad preferences: {0}", ex.GetType().Name);
            return new();
        }
    }

    internal static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    internal ObservableCollection<string> GetSharedRecents(IEnumerable<string> initial) =>
        _sharedRecents ??= new(initial.Distinct(PathComparer).Take(10));

    public virtual void Save(AppSettings settings)
    {
        lock (WriteLock)
            Write(settings);
    }

    private void Write(AppSettings settings)
    {
        string folder = Path.GetDirectoryName(_settingsPath)!;
        Directory.CreateDirectory(folder);
        string staging = Path.Combine(folder, $".settings-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(staging, FileMode.CreateNew))
                JsonSerializer.Serialize(stream, settings, AppSettingsContext.Default.AppSettings);
            File.Move(staging, _settingsPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(staging)) File.Delete(staging);
        }
    }

    private void Update(Action<AppSettings> update)
    {
        lock (WriteLock)
        {
            var settings = Load();
            update(settings);
            Write(settings);
        }
    }

    public EditorSettings LoadEditorSettings() => Load().Editor;
    public void SaveEditorSettings(EditorSettings settings) => Update(all => all.Editor = settings);
    public SearchSettings LoadSearchSettings() => Load().Search;
    public void SaveSearchSettings(SearchSettings settings) => Update(all => all.Search = settings);
    public PageSetupSettings LoadPageSetupSettings() => Load().PageSetup;
    public void SavePageSetupSettings(PageSetupSettings settings) => Update(all => all.PageSetup = settings);
    public List<string> LoadRecentFiles() => Load().RecentFiles;
    public void SaveRecentFiles(IEnumerable<string> files) => Update(all => all.RecentFiles = new(files));
}

public class AppSettings
{
    public EditorSettings Editor { get; set; } = new();
    public SearchSettings Search { get; set; } = new();
    public PageSetupSettings PageSetup { get; set; } = new();
    public List<string> RecentFiles { get; set; } = new();
}
