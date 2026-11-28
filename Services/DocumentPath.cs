using System;
using System.IO;

namespace NotepadAvalonia.Services;

internal static class DocumentPath
{
    internal static bool IsAnsiArt(string path) => Path.GetExtension(path).ToLowerInvariant() is ".nfo" or ".diz";

    internal static string GetFullPath(string path)
    {
        path = Path.GetFullPath(path);
        if (OperatingSystem.IsWindows())
        {
            string root = Path.GetPathRoot(path)!;
            if (root.StartsWith(@"\\.\", StringComparison.Ordinal) ||
                (root.StartsWith(@"\\?\", StringComparison.Ordinal) &&
                 !(root.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase) ||
                   root.StartsWith(@"\\?\Volume{", StringComparison.OrdinalIgnoreCase) ||
                   (root.Length == 7 && char.IsAsciiLetter(root[4]) && root[5] == ':' && root[6] == '\\'))) ||
                path.AsSpan(root.Length).Contains(':') || !IsValidName(Path.GetFileName(path)))
                throw new IOException("Choose an ordinary Windows file path. Device names and alternate data streams are unsupported.");
        }
        return path;
    }

    internal static bool IsValidName(string name) => name.Length != 0 &&
        name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
        (!OperatingSystem.IsWindows() || !IsWindowsDeviceName(name));

    private static bool IsWindowsDeviceName(string name)
    {
        ReadOnlySpan<char> stem = name.AsSpan();
        int extension = stem.IndexOf('.');
        if (extension >= 0) stem = stem[..extension];
        stem = stem.Trim(' ');
        return stem.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("CONIN$", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("CONOUT$", StringComparison.OrdinalIgnoreCase) ||
            (stem.Length == 4 && (stem[3] is >= '1' and <= '9' or '¹' or '²' or '³') &&
                (stem[..3].Equals("COM", StringComparison.OrdinalIgnoreCase) ||
                 stem[..3].Equals("LPT", StringComparison.OrdinalIgnoreCase)));
    }
}
