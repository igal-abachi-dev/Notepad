using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using NotepadAvalonia.Services.Tools;
using Xunit;

namespace Notepad.Tests;

public class ScriptLaunchTests
{
    [Fact]
    public async Task BatchLaunch_PassesPercentAndShellPunctuationAsPartOfTheFilename()
    {
        if (!OperatingSystem.IsWindows()) return;
        var folder = Directory.CreateTempSubdirectory("notepad-script-");
        try
        {
            string script = Path.Combine(folder.FullName, "one %NOTEPAD_TEST_VARIABLE% & (batch) !.cmd");
            await File.WriteAllTextAsync(script, "@echo on-disk script>proof.txt\r\n");
            var start = ScriptLaunch.CreateStartInfo(script, Environment.SystemDirectory, "");
            Assert.Equal(script, start.Environment["NOTEPAD_SCRIPT_FILE"]);
            Assert.DoesNotContain(script, start.Arguments);
            start.Arguments = start.Arguments.Replace("/K", "/C", StringComparison.Ordinal);
            start.CreateNoWindow = true;
            using var process = Assert.IsType<Process>(Process.Start(start));
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, process.ExitCode);
            Assert.Equal("on-disk script", (await File.ReadAllTextAsync(Path.Combine(folder.FullName, "proof.txt"))).Trim());
        }
        finally { folder.Delete(recursive: true); }
    }

    [Fact]
    public void PowerShellLaunch_PassesTheScriptThroughTheArgumentListAndKeepsExecutionPolicy()
    {
        string path = Path.Combine(Path.GetTempPath(), "script & name.ps1");
        var start = ScriptLaunch.CreateStartInfo(path, Path.GetTempPath(), "");
        Assert.EndsWith("powershell.exe", start.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new[] { "-NoProfile", "-NoExit", "-File", path }, start.ArgumentList);
        Assert.Equal(Path.GetDirectoryName(path), start.WorkingDirectory);
    }
}
