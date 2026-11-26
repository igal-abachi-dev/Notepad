using System;

namespace Notepad.NeoEdit;

public sealed partial class NeoEditor
{
    public event EventHandler<Uri>? WebLinkRequested;

    internal Uri? GetWebLinkAt(int offset)
    {
        const int maxLength = 2048;
        int start = Math.Max(0, offset - maxLength);
        int end = offset + Math.Min(_doc.Length - offset, maxLength);
        string surrounding = _doc.GetTextInRange(start, end - start);
        int position = offset - start;
        int first = position;
        int last = position;
        while (first > 0 && !IsLinkDelimiter(surrounding[first - 1])) first--;
        while (last < surrounding.Length && !IsLinkDelimiter(surrounding[last])) last++;
        if (last <= first || last - first > maxLength) return null;
        var token = surrounding.AsSpan(first, last - first);
        int scheme = token.IndexOf("https://", StringComparison.OrdinalIgnoreCase);
        int http = token.IndexOf("http://", StringComparison.OrdinalIgnoreCase);
        if (scheme < 0 || (http >= 0 && http < scheme)) scheme = http;
        if (scheme < 0 || first + scheme > position) return null;
        token = token[scheme..].TrimEnd(".,;!?");
        if (!token.IsEmpty && token[^1] == ')' && token.Count(')') > token.Count('(')) token = token[..^1];
        if (!token.IsEmpty && token[^1] == ']' && token.Count(']') > token.Count('[')) token = token[..^1];
        return position <= first + scheme + token.Length &&
            Uri.TryCreate(token.ToString(), UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) && uri.Host.Length > 0 ? uri : null;
    }

    private static bool IsLinkDelimiter(char character) =>
        char.IsWhiteSpace(character) || char.IsControl(character) || character is '<' or '>' or '"' or '\'';
}
