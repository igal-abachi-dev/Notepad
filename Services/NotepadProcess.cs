using System;
using System.Diagnostics;
using System.IO;

namespace NotepadAvalonia.Services;

internal static class NotepadProcess
{
    public static ProcessStartInfo CreateStartInfo(string? executable, string appDirectory, params string[] arguments)
    {
        if (string.IsNullOrEmpty(executable))
            throw new IOException("Cannot locate the running Notepad executable.");

        var start = new ProcessStartInfo(executable) { UseShellExecute = false };
        if (string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Path.Combine(appDirectory, "Notepad.dll"));
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        return start;
    }
}
