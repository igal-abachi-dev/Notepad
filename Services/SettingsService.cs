using NotepadAvalonia.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;

namespace NotepadAvalonia.Services;

[JsonSerializable(typeof(AppSettings))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal partial class AppSettingsContext : JsonSerializerContext;

public class SettingsService
{
    private static readonly object WriteLock = new();
    private readonly string _settingsPath;
    private ObservableCollection<string>? _sharedRecents;
    private JsonObject? _baseline;

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
            if (!Enum.IsDefined(settings.Editor.Theme)) settings.Editor.Theme = EditorTheme.System;
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

    internal void TrackChanges(AppSettings settings) => _baseline = AsObject(settings);

    public virtual void Save(AppSettings settings)
    {
        lock (WriteLock)
        {
            using var fileLock = AcquireWriteLock();
            var local = AsObject(settings);
            if (_baseline == null)
                Write(local);
            else
            {
                var latest = Load();
                var merged = AsObject(latest);
                foreach (string group in new[] { nameof(AppSettings.Editor), nameof(AppSettings.Search), nameof(AppSettings.PageSetup) })
                {
                    var destination = merged[group]!.AsObject();
                    var previous = _baseline[group]!.AsObject();
                    foreach (var (key, value) in local[group]!.AsObject())
                        if (!JsonNode.DeepEquals(value, previous[key])) destination[key] = value?.DeepClone();
                }

                const string recents = nameof(AppSettings.RecentFiles);
                if (!JsonNode.DeepEquals(local[recents], _baseline[recents]))
                {
                    var removed = _baseline[recents]!.AsArray().Select(path => path!.GetValue<string>())
                        .Except(settings.RecentFiles, PathComparer).ToHashSet(PathComparer);
                    var recent = settings.RecentFiles.Concat(latest.RecentFiles.Where(path => !removed.Contains(path)))
                        .Distinct(PathComparer).Take(10);
                    merged[recents] = new JsonArray(recent.Select(path => (JsonNode?)JsonValue.Create(path)).ToArray());
                }
                Write(merged);
            }
            _baseline = local;
        }
    }

    private static JsonObject AsObject(AppSettings settings) =>
        JsonSerializer.SerializeToNode(settings, AppSettingsContext.Default.AppSettings)!.AsObject();

    private FileStream AcquireWriteLock()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        for (int attempt = 0; ; attempt++)
        {
            try { return new FileStream(_settingsPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (attempt < 5) { Thread.Sleep(20); }
        }
    }

    private void Write(JsonObject settings)
    {
        string folder = Path.GetDirectoryName(_settingsPath)!;
        Directory.CreateDirectory(folder);
        string staging = Path.Combine(folder, $".settings-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(staging, FileMode.CreateNew))
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
                settings.WriteTo(writer);
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
            using var fileLock = AcquireWriteLock();
            var settings = Load();
            update(settings);
            Write(AsObject(settings));
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
