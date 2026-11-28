using System;

namespace NotepadAvalonia.Services.Tools;

internal static class FencedSelection
{
    internal static (string Text, string? Extension) Extract(string text)
    {
        var source = text.AsSpan();
        var trimmed = source.Trim();
        if (trimmed.Length < 3 || trimmed[0] is not ('\u0060' or '~')) return (text, null);
        // Work against the untrimmed source so the script's original indentation and endings survive.
        int opening = source.IndexOf(trimmed);
        if (opening > 3 || source[..opening].ContainsAnyExcept(' ')) return (text, null);
        char delimiter = source[opening];
        int fenceLength = FenceLength(source[opening..], delimiter);
        int lineEnd = source.IndexOfAny('\r', '\n');
        if (fenceLength < 3 || lineEnd < opening + fenceLength) return (text, null);
        var info = source[(opening + fenceLength)..lineEnd].Trim();
        int space = info.IndexOfAny(' ', '\t');
        if (space >= 0) info = info[..space];
        string? extension = info.ToString().ToLowerInvariant() switch
        {
            "powershell" or "pwsh" or "ps1" => ".ps1",
            "batch" or "bat" => ".bat",
            "cmd" => ".cmd",
            "python" or "py" => ".py",
            "javascript" or "js" => ".js",
            "typescript" or "ts" => ".ts",
            "json" => ".json",
            "xml" => ".xml",
            "html" => ".html",
            "csharp" or "cs" => ".cs",
            "sql" => ".sql",
            _ => null
        };
        if (extension == null) return (text, null);

        int start = AfterLineEnding(source, lineEnd);
        for (int line = start; line < source.Length;)
        {
            int end = source[line..].IndexOfAny('\r', '\n');
            end = end < 0 ? source.Length : line + end;
            var possibleFence = source[line..end].TrimStart(' ');
            int closing = FenceLength(possibleFence, delimiter);
            if (closing >= fenceLength && possibleFence[closing..].Trim().IsEmpty && end - line - possibleFence.Length <= 3)
                return source[end..].Trim().IsEmpty ? (text[start..line], extension) : (text, null);
            line = AfterLineEnding(source, end);
        }
        return (text, null);
    }

    internal static bool IsSuggestedExtension(string? extension) => extension is
        ".ps1" or ".bat" or ".cmd" or ".py" or ".js" or ".ts" or ".json" or ".xml" or ".html" or ".cs" or ".sql";

    private static int FenceLength(ReadOnlySpan<char> text, char delimiter)
    {
        int end = 0;
        while (end < text.Length && text[end] == delimiter) end++;
        return end;
    }

    private static int AfterLineEnding(ReadOnlySpan<char> text, int end) => end == text.Length ? end :
        end + (text[end] == '\r' && end + 1 < text.Length && text[end + 1] == '\n' ? 2 : 1);
}
