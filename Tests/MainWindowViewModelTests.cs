using System;
using System.IO;
using System.Threading.Tasks;
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
    public void ToggleWordWrap_DisablesStatusBarAndGoTo()
    {
        var settingsService = CreateSettingsService(out _);
        var vm = new MainWindowViewModel(new FileService(), new SearchService(), settingsService, new PrintService());

        vm.ToggleWordWrapCommand.Execute(null);

        Assert.True(vm.Settings.WordWrap);
        Assert.False(vm.Settings.ShowStatusBar);
        Assert.False(vm.CanUseGoToLine);
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
