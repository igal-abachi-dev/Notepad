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
    public void ToggleWordWrap_DisablesGoToAndHidesStatusBar()
    {
        var settingsService = CreateSettingsService(out _);
        var vm = new MainWindowViewModel(new FileService(), new SearchService(), settingsService, new PrintService());

        vm.ToggleWordWrapCommand.Execute(null);

        Assert.True(vm.Settings.WordWrap);
        Assert.False(vm.Settings.ShowStatusBar);
        Assert.False(vm.CanUseGoToLine);
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

    [Fact]
    public async Task RecentFiles_CapsAtTenAndKeepsMostRecentFirst()
    {
        var settingsService = CreateSettingsService(out _);
        var vm = new MainWindowViewModel(new FileService(), new SearchService(), settingsService, new PrintService());

        for (int i = 0; i < 12; i++)
        {
            var path = Path.Combine(Path.GetTempPath(), $"notepad_test_{i}.txt");
            await File.WriteAllTextAsync(path, $"file {i}");
            await vm.OpenFileAsync(path);
        }

        Assert.Equal(10, vm.RecentFiles.Count);
        Assert.EndsWith("notepad_test_11.txt", vm.RecentFiles[0]);
    }
}
