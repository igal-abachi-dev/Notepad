using System;
using System.Diagnostics;
using System.IO;

namespace NotepadAvalonia.Services.Tools;

internal static class ScriptLaunch
{
    internal static bool IsScriptPath(string path) => Path.GetExtension(path).ToLowerInvariant() is ".ps1" or ".cmd" or ".bat";

    internal static ProcessStartInfo CreateStartInfo(string path, string systemDirectory, string programFiles)
    {
        if (!Path.IsPathFullyQualified(path) || path.AsSpan().IndexOfAny("\"\r\n\0") >= 0 || !IsScriptPath(path))
            throw new IOException("Choose a saved PowerShell or batch script.");
        string directory = Path.GetDirectoryName(path)!;
        if (Path.GetExtension(path).Equals(".ps1", StringComparison.OrdinalIgnoreCase))
        {
            string preferred = Path.Combine(programFiles, "PowerShell", "7", "pwsh.exe");
            string executable = !string.IsNullOrEmpty(programFiles) && File.Exists(preferred)
                ? preferred : Path.Combine(systemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            return new ProcessStartInfo(executable)
            {
                WorkingDirectory = directory,
                UseShellExecute = false,
                ArgumentList = { "-NoProfile", "-NoExit", "-File", path }
            };
        }

        var start = new ProcessStartInfo(Path.Combine(systemDirectory, "cmd.exe"))
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
            // Expand one quoted environment variable instead of inserting a filename into cmd syntax.
            Arguments = "/D /V:OFF /S /K \"\"%NOTEPAD_SCRIPT_FILE%\"\""
        };
        start.Environment["NOTEPAD_SCRIPT_FILE"] = path;
        return start;
    }
}
