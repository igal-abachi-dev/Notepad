using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using System;

namespace Notepad.NeoEdit;

public sealed partial class NeoEditor
{
    public event EventHandler<Uri>? WebLinkRequested;

    internal readonly record struct WebLinkRange(int Start, int Length, Uri Uri);
    private WebLinkRange? _hoveredLink;
    private Point? _lastLinkPoint;
    private Cursor? _linkCursor;
    private Cursor? _cursorBeforeLink;
    private static KeyModifiers LinkModifier => OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;

    internal Uri? GetWebLinkAt(int offset) => GetWebLinkRangeAt(offset)?.Uri;

    internal WebLinkRange? GetWebLinkRangeAt(int offset)
    {
        if (offset < 0 || offset >= _doc.Length) return null;
        const int maxLength = 2048;
        int windowStart = Math.Max(0, offset - maxLength);
        int windowEnd = offset + Math.Min(_doc.Length - offset, maxLength + 1);
        string text = _doc.GetTextInRange(windowStart, windowEnd - windowStart);
        int hit = offset - windowStart;
        int start = hit;
        while (start > 0 && !IsLinkDelimiter(text[start - 1])) start--;

        for (int search = start; search <= hit;)
        {
            int next = text.IndexOf("http", search, StringComparison.OrdinalIgnoreCase);
            if (next < 0 || next > hit) return null;
            search = next + 4;
            var rest = text.AsSpan(next);
            if (!rest.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
                !rest.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) continue;
            if (next > 0 && (char.IsLetterOrDigit(text[next - 1]) || text[next - 1] is '_' or '+')) continue;

            int end = next, parentheses = 0, brackets = 0;
            for (; end < text.Length && !IsLinkDelimiter(text[end]); end++)
            {
                switch (text[end])
                {
                    case '(':
                        parentheses++;
                        break;
                    case ')':
                        if (parentheses-- == 0) goto EndOfLink;
                        break;
                    case '[':
                        brackets++;
                        break;
                    case ']':
                        if (brackets-- == 0) goto EndOfLink;
                        break;
                }
            }
            EndOfLink:
            if (end == text.Length && windowEnd < _doc.Length || end - next > maxLength) continue;
            bool markdown = next >= 2 && text.AsSpan(next - 2, 2).SequenceEqual("](");
            var candidate = text.AsSpan(next, end - next);
            if (!markdown) candidate = candidate.TrimEnd(".,;!?");
            if (hit < next + candidate.Length && Uri.TryCreate(candidate.ToString(), UriKind.Absolute, out var uri) && uri.Host.Length > 0)
                return new(windowStart + next, candidate.Length, uri);
        }
        return null;
    }

    private WebLinkRange? HitWebLink(Point point)
    {
        if (WebLinkRequested == null || point.X < TextLeft || point.Y < 0 || point.Y >= Bounds.Height) return null;
        int offset = GetOffsetAt(point.X - TextLeft + Offset.X, point.Y + Offset.Y, out _, out bool insideText, linkHit: true);
        if (!insideText) return null;
        return _hoveredLink is { } link && offset >= link.Start && offset - link.Start < link.Length
            ? link : GetWebLinkRangeAt(offset);
    }

    private void SetHoveredLink(WebLinkRange? link)
    {
        if (_hoveredLink == link) return;
        if (link.HasValue && !_hoveredLink.HasValue)
        {
            _cursorBeforeLink = Cursor;
            Cursor = _linkCursor ??= new Cursor(StandardCursorType.Hand);
        }
        else if (!link.HasValue)
        {
            Cursor = _cursorBeforeLink;
            _cursorBeforeLink = null;
        }
        _hoveredLink = link;
        InvalidateVisual();
    }

    private void DrawHoveredLink(DrawingContext context, LineLayout row, Point origin, int start)
    {
        if (_hoveredLink is not { } link || link.Start < start || link.Start >= start + row.Length) return;
        int from = row.ToVisual(link.Start - start);
        int to = row.ToVisual(Math.Min(row.Length, link.Start - start + link.Length));
        var pen = new Pen(Foreground, 1);
        foreach (var segment in row.Layout.HitTestTextRange(from, to - from))
        {
            double y = origin.Y + segment.Bottom - 1;
            context.DrawLine(pen, new Point(origin.X + segment.X, y), new Point(origin.X + segment.Right, y));
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _lastLinkPoint = null;
        SetHoveredLink(null);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.KeyModifiers != LinkModifier) SetHoveredLink(null);
    }

    private static bool IsLinkDelimiter(char character) =>
        char.IsWhiteSpace(character) || char.IsControl(character) || character is '<' or '>' or '"' or '\'' or '\u0060';
}
