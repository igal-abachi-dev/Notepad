using AvaloniaNotePad;
using NotepadAvalonia.Models;
using NotepadAvalonia.Services;
using System;
using System.IO;
using Xunit;

namespace Notepad.Tests;

public class StartupTests
{
    [Theory]
    [InlineData("file name.txt", "/p", "/W", "file name.txt", FileEncodingType.UTF16LE, true)]
    [InlineData("/A", "/P", "file.txt", "file.txt", FileEncodingType.ANSI, true)]
    [InlineData("file.txt", "--", "/P", "file.txt", null, false)]
    [InlineData("--", "/P", "/A", "/P", null, false)]
    public void CommandLine_RecognizesSwitchesBeforeAndAfterTheFilename(
        string first, string second, string third, string filename, FileEncodingType? encoding, bool print)
    {
        var result = App.ParseStartupArguments([first, second, third]);
        Assert.Equal((filename, encoding, print, (Guid?)null), result);
    }

    [Fact]
    public void CommandLine_PreservesAWhitespaceFilenameOnUnix()
    {
        var result = App.ParseStartupArguments([" ", "next.txt"]);
        Assert.Equal(OperatingSystem.IsWindows() ? "next.txt" : " ", result.Path);
    }

    [Fact]
    public void NewWindow_StartsThePublishedExecutableWithNoInheritedArguments()
    {
        var executable = Path.GetFullPath("Notepad.exe");
        var process = NotepadProcess.CreateStartInfo(executable, Path.GetTempPath());
        Assert.Equal(executable, process.FileName);
        Assert.Empty(process.ArgumentList);
        Assert.False(process.UseShellExecute);
    }

    [Fact]
    public void FrameworkDependentLaunch_PassesTheAssemblyAndRecoveryIdAsSeparateArguments()
    {
        string path = Path.Combine(Path.GetTempPath(), "example path");
        var id = Guid.NewGuid();
        var arguments = new[] { "--recover-draft", id.ToString("N") };
        var process = NotepadProcess.CreateStartInfo(Path.Combine(path, "dotnet.exe"), path, arguments);
        Assert.Equal(new[] { Path.Combine(path, "Notepad.dll"), arguments[0], arguments[1] }, process.ArgumentList);
        Assert.Equal(id, App.ParseStartupArguments(arguments).DraftId);
    }

    [Fact]
    public void SeparateSettingsInstances_MergeOnlyLocallyChangedPreferences()
    {
        var folder = Directory.CreateTempSubdirectory("notepad-settings-process-");
        try
        {
            string file = Path.Combine(folder.FullName, "settings.json");
            var first = new SettingsService(file);
            var second = new SettingsService(file);
            var a = first.Load();
            var b = second.Load();
            first.TrackChanges(a);
            second.TrackChanges(b);
            a.Editor.DefaultEncoding = FileEncodingType.UTF8BOM;
            a.RecentFiles.Add("from-first");
            b.Editor.Theme = EditorTheme.Dark;
            b.RecentFiles.Add("from-second");
            first.Save(a);
            second.Save(b);
            first.Save(a);
            var merged = second.Load();
            Assert.Equal(FileEncodingType.UTF8BOM, merged.Editor.DefaultEncoding);
            Assert.Equal(EditorTheme.Dark, merged.Editor.Theme);
            Assert.Equal(new[] { "from-second", "from-first" }, merged.RecentFiles);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }
}
