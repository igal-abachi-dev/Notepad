using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Notepad.NeoEdit;
using NotepadAvalonia.Models;
using NotepadAvalonia.Services;
using NotepadAvalonia.ViewModels;
using Xunit;

namespace Notepad.Tests;

public class MainWindowViewModelTests
{
    private static SettingsService CreateSettingsService(out string tempPath)
    {
        var dir = Path.Combine(Path.GetTempPath(), "NotepadTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        tempPath = Path.Combine(dir, "settings.json");
        return new SettingsService(tempPath);
    }

    [Fact]
    public void ToggleWordWrap_DisablesGoToAndPersistsStatusBarIndependently()
    {
        var settingsService = CreateSettingsService(out _);
        var vm = new MainWindowViewModel(new FileService(), new SearchService(), settingsService, new PrintService());

        vm.ToggleWordWrapCommand.Execute(null);

        Assert.True(vm.Settings.WordWrap);
        Assert.True(vm.Settings.ShowStatusBar);
        Assert.False(vm.CanUseGoToLine);

        var reopened = new MainWindowViewModel(new FileService(), new SearchService(), settingsService, new PrintService());
        Assert.True(reopened.Settings.WordWrap);
        reopened.ToggleWordWrapCommand.Execute(null);
        Assert.True(reopened.Settings.ShowStatusBar);
        reopened.ToggleStatusBarCommand.Execute(null);
        reopened.ToggleWordWrapCommand.Execute(null);
        Assert.False(settingsService.LoadEditorSettings().ShowStatusBar);
    }

    [AvaloniaFact]
    public void LoadDocument_EmptyLoadedText_ClearsTypedContentsAndHistory()
    {
        var editor = new NeoEditor();
        editor.InsertAtCaret("discard this");
        editor.LoadDocument("");
        editor.Undo();

        Assert.Equal("", editor.GetText());
        Assert.Equal(0, editor.CaretOffset);
    }

    [AvaloniaFact]
    public async Task OpenFile_SameContent_ResetsEditorAndUndoHistory()
    {
        var settingsService = CreateSettingsService(out var settingsPath);
        var vm = new MainWindowViewModel(new FileService(), new SearchService(), settingsService, new PrintService());
        var editor = new NeoEditor();
        vm.InitializeEditor(editor);
        string path = Path.Combine(Path.GetDirectoryName(settingsPath)!, "document.txt");
        try
        {
            await File.WriteAllTextAsync(path, "first\r\nsecond");
            await vm.LoadFileAsync(path);
            editor.InsertAtCaret("discard this");
            Assert.True(vm.Document.IsModified);

            await vm.LoadFileAsync(path);
            editor.Undo();

            Assert.Equal("first\r\nsecond", editor.GetText());
            Assert.Equal(0, editor.CaretOffset);
            Assert.False(vm.Document.IsModified);
        }
        finally
        {
            File.Delete(path);
            File.Delete(settingsPath);
        }
    }

    [AvaloniaFact]
    public async Task UndoAndRedo_AfterSave_ReportsUnsavedChanges()
    {
        var settingsService = CreateSettingsService(out var settingsPath);
        var vm = new MainWindowViewModel(new FileService(), new SearchService(), settingsService, new PrintService());
        var editor = new NeoEditor();
        vm.InitializeEditor(editor);
        string path = Path.Combine(Path.GetDirectoryName(settingsPath)!, "document.txt");
        try
        {
            await File.WriteAllTextAsync(path, "a");
            await vm.LoadFileAsync(path);
            editor.InsertAtCaret("b");
            await vm.SaveFileCommand.ExecuteAsync(null);
            Assert.False(vm.Document.IsModified);

            editor.Undo();
            Assert.True(vm.Document.IsModified);
            editor.Redo();
            Assert.False(vm.Document.IsModified);
            editor.Undo();
            await vm.SaveFileCommand.ExecuteAsync(null);
            Assert.False(vm.Document.IsModified);

            editor.Redo();
            Assert.True(vm.Document.IsModified);
        }
        finally
        {
            File.Delete(path);
            File.Delete(settingsPath);
        }
    }

    [AvaloniaFact]
    public async Task Save_WithCompetingRevisionsAndFailingPreferences_CommitsInOrder()
    {
        using var files = new TemporaryDirectory();
        var path = Path.Combine(files.Path, "document.txt");
        await File.WriteAllTextAsync(path, "a");
        var settings = new FailingSettings(Path.Combine(files.Path, "settings.json"));
        var writer = new BlockingWriter();
        var vm = new MainWindowViewModel(writer, new SearchService(), settings, new PrintService());
        var editor = new NeoEditor();
        vm.InitializeEditor(editor);
        Assert.True(await vm.LoadFileAsync(path));
        editor.CaretOffset = 1;
        editor.InsertAtCaret("1");

        var first = vm.SaveFileCommand.ExecuteAsync(null);
        await writer.FirstStarted.Task;
        editor.InsertAtCaret("2");
        var second = vm.SaveFileCommand.ExecuteAsync(null);
        Assert.Equal(1, writer.Calls);

        writer.ReleaseFirst.SetResult();
        await Task.WhenAll(first, second);
        Assert.Equal(2, writer.Calls);
        Assert.Equal("a12", await File.ReadAllTextAsync(path));
        Assert.Equal(path, vm.Document.FilePath);
        Assert.False(vm.Document.IsModified);
        Assert.Equal("Preferences could not be saved.", vm.SettingsWarning);
    }

    [AvaloniaFact]
    public async Task Save_AnEditAfterTheSnapshot_RemainsDirtyUntilUndo()
    {
        using var files = new TemporaryDirectory();
        var path = Path.Combine(files.Path, "document.txt");
        await File.WriteAllTextAsync(path, "a");
        var writer = new BlockingWriter();
        var vm = new MainWindowViewModel(writer, new SearchService(), new SettingsService(Path.Combine(files.Path, "prefs.json")), new PrintService());
        var editor = new NeoEditor();
        vm.InitializeEditor(editor);
        await vm.LoadFileAsync(path);
        editor.CaretOffset = 1;
        editor.InsertAtCaret("1");
        var save = vm.SaveFileCommand.ExecuteAsync(null);
        await writer.FirstStarted.Task;
        editor.InsertAtCaret("2");
        writer.ReleaseFirst.SetResult();
        await save;

        Assert.Equal("a1", await File.ReadAllTextAsync(path));
        Assert.True(vm.Document.IsModified);
        editor.Undo();
        Assert.False(vm.Document.IsModified);
    }

    [AvaloniaFact]
    public async Task Load_LogMarkerAndMalformedData_PreserveOriginalBytesAndExistingDocument()
    {
        using var files = new TemporaryDirectory();
        var log = Path.Combine(files.Path, "log.txt");
        var lower = Path.Combine(files.Path, "lower.txt");
        var malformed = Path.Combine(files.Path, "invalid.txt");
        await File.WriteAllTextAsync(log, ".LOGIC\nentry");
        await File.WriteAllTextAsync(lower, ".log");
        await File.WriteAllBytesAsync(malformed, [0xFF, 0xFE, 0x41, 0x00, 0x42]);
        var clock = new FixedClock();
        var vm = new MainWindowViewModel(new FileService(), new SearchService(),
            new SettingsService(Path.Combine(files.Path, "prefs.json")), new PrintService(), clock);
        var editor = new NeoEditor();
        vm.InitializeEditor(editor);

        Assert.True(await vm.LoadFileAsync(log));
        var local = clock.GetLocalNow().DateTime;
        Assert.Equal($".LOGIC\nentry\r\n{local:t} {local:d}\r\n", editor.GetText());
        Assert.Equal(editor.GetText().Length, editor.CaretOffset);
        Assert.True(vm.Document.IsModified);
        Assert.Equal(".LOGIC\nentry", await File.ReadAllTextAsync(log));
        Assert.False(await vm.LoadFileAsync(malformed));
        Assert.Equal(log, vm.Document.FilePath);
        editor.Undo();
        Assert.Equal(".LOGIC\nentry", editor.GetText());
        Assert.False(vm.Document.IsModified);

        Assert.True(await vm.LoadFileAsync(lower));
        Assert.Equal(".log", editor.GetText());
        Assert.False(vm.Document.IsModified);
    }

    [Fact]
    public void Settings_CorruptFileLoadsDefaultsAndCanBeSaved()
    {
        using var files = new TemporaryDirectory();
        var path = Path.Combine(files.Path, "settings.json");
        File.WriteAllText(path, "{ invalid");
        var settings = new SettingsService(path);
        Assert.True(settings.LoadEditorSettings().ShowStatusBar);
        settings.SaveEditorSettings(new EditorSettings { WordWrap = true });
        Assert.True(settings.LoadEditorSettings().WordWrap);
        Assert.Equal(path, Assert.Single(Directory.GetFiles(files.Path, "*.json")));
        Assert.Empty(Directory.GetFiles(files.Path, "*.tmp"));
    }

    [AvaloniaFact]
    public async Task Open_ObsoleteReadsCannotReplaceNewerDocumentsOrUserEdits()
    {
        using var files = new TemporaryDirectory();
        var older = Path.Combine(files.Path, "older.txt");
        var newer = Path.Combine(files.Path, "newer.txt");
        await File.WriteAllTextAsync(older, "old");
        await File.WriteAllTextAsync(newer, "new");
        var service = new WaitingReader(older);
        var vm = new MainWindowViewModel(service, new SearchService(),
            new SettingsService(Path.Combine(files.Path, "prefs.json")), new PrintService());
        var editor = new NeoEditor();
        vm.InitializeEditor(editor);

        var pending = vm.LoadFileAsync(older);
        Assert.True(await vm.LoadFileAsync(newer));
        service.Release.SetResult();
        Assert.False(await pending);
        Assert.Equal(newer, vm.Document.FilePath);
        Assert.Equal("new", editor.Text);

        service.Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        pending = vm.LoadFileAsync(older);
        editor.InsertAtCaret("edited ");
        service.Release.SetResult();
        Assert.False(await pending);
        Assert.Equal("edited new", editor.Text);
        Assert.True(vm.Document.IsModified);
    }

    private sealed class WaitingReader(string pathToBlock) : FileService
    {
        public TaskCompletionSource Release { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async Task<LoadedFile> ReadDocumentAsync(string path, FileEncodingType? requestedEncoding = null, bool windowsOnly = false)
        {
            if (path == pathToBlock) await Release.Task;
            return await base.ReadDocumentAsync(path, requestedEncoding, windowsOnly);
        }
    }

    private sealed class FailingSettings(string path) : SettingsService(path)
    {
        public override void Save(AppSettings settings) => throw new IOException("Test preferences failure.");
    }

    private sealed class BlockingWriter : FileService
    {
        public TaskCompletionSource FirstStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFirst { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls { get; private set; }

        public override async Task<FileVersion> CommitFileAsync(string path, string content, FileEncodingType encodingType,
            LineEndingStyle lineEnding, FileVersion? expected = null)
        {
            if (++Calls == 1)
            {
                FirstStarted.SetResult();
                await ReleaseFirst.Task;
            }
            return await base.CommitFileAsync(path, content, encodingType, lineEnding, expected);
        }
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2025, 3, 4, 12, 36, 0, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("notepad-tests-").FullName;
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    [Fact]
    public async Task RecentFiles_CapsAtTenAndKeepsMostRecentFirst()
    {
        using var folder = new TemporaryDirectory();
        var settingsService = new SettingsService(Path.Combine(folder.Path, "settings.json"));
        var vm = new MainWindowViewModel(new FileService(), new SearchService(), settingsService, new PrintService());

        for (int i = 0; i < 12; i++)
        {
            var path = Path.Combine(folder.Path, $"notepad_test_{i}.txt");
            await File.WriteAllTextAsync(path, $"file {i}");
            await vm.OpenFileAsync(path);
        }

        Assert.Equal(10, vm.RecentFiles.Count);
        Assert.EndsWith("notepad_test_11.txt", vm.RecentFiles[0]);
    }
}
