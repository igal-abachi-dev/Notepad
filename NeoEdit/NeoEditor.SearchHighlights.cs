using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using System;
using System.Collections.Generic;

namespace Notepad.NeoEdit;

public sealed partial class NeoEditor
{
    private static readonly IBrush LightSearchFill = Brush.Parse("#10D8991A");
    private static readonly IBrush DarkSearchFill = Brush.Parse("#35C78618");
    private static readonly Pen LightSearchOutline = new(Brush.Parse("#996100"));
    private static readonly Pen DarkSearchOutline = new(Brush.Parse("#E8BB68"));
    private IReadOnlyList<(int Start, int Length)> _passiveSearchMatches = [];
    private long _searchPreviewRevision = -1;
    private (int Start, int Length, long Revision, long Selection)? _activeSearchMatch;

    internal IReadOnlyList<(int Start, int Length)> PassiveSearchMatches =>
        _searchPreviewRevision == DocumentVersion ? _passiveSearchMatches : [];

    internal (int Start, int Length)? ActiveSearchMatch =>
        _activeSearchMatch is { } match && match.Revision == DocumentVersion && match.Selection == SelectionVersion
            ? (match.Start, match.Length) : null;

    internal void SelectSearchMatch(int start, int length)
    {
        SetSelection(start, length);
        _activeSearchMatch = (SelectionStart, SelectionLength, DocumentVersion, SelectionVersion);
        InvalidateVisual();
    }

    internal void ClearActiveSearchMatch()
    {
        if (_activeSearchMatch == null) return;
        _activeSearchMatch = null;
        InvalidateVisual();
    }

    internal void ClearSearchHighlights()
    {
        ClearActiveSearchMatch();
        SetPassiveSearchMatches(-1, []);
    }

    internal void SetPassiveSearchMatches(long revision, IReadOnlyList<(int Start, int Length)> matches)
    {
        _searchPreviewRevision = revision;
        _passiveSearchMatches = matches;
        InvalidateVisual();
    }

    internal string? GetSearchPreviewSnapshot() => _snapshot ?? (TextLength <= 1024 * 1024 ? GetText() : null);

    internal string GetSearchPreviewText(int offset, int length) => _doc.GetTextInRange(offset, length);

    internal (int Start, int End) GetVisibleTextRange()
    {
        int first = LineAtY(Offset.Y);
        int last = LineAtY(Offset.Y + Bounds.Height);
        int start = _doc.GetOffsetFromLineColumn(first, 0);
        int end = last + 1 < LineCount ? _doc.GetOffsetFromLineColumn(last + 1, 0) : TextLength;
        if (WordWrap)
        {
            var firstRow = GetLineLayout(first);
            var firstSegments = firstRow.Layout.TextLines;
            int top = Math.Clamp((int)Math.Floor((Offset.Y - TopOfLine(first)) / LineHeight), 0, firstSegments.Count - 1);
            start += firstRow.ToDocument(firstSegments[top].FirstTextSourceIndex);

            var lastRow = GetLineLayout(last);
            var lastSegments = lastRow.Layout.TextLines;
            int bottom = Math.Clamp((int)Math.Ceiling((Offset.Y + Bounds.Height - TopOfLine(last)) / LineHeight) - 1,
                0, lastSegments.Count - 1);
            if (bottom < lastSegments.Count - 1)
            {
                var segment = lastSegments[bottom];
                end = _doc.GetOffsetFromLineColumn(last, lastRow.ToDocument(segment.FirstTextSourceIndex + segment.Length));
            }
        }
        else if (first == last && Offset.X > 0 &&
            GetLineLayout(first) is { HasBidi: false, Direction: FlowDirection.LeftToRight } row)
        {
            start = HitRow(first, row, Offset.X, 0, out _, out _);
            end = Math.Min(end, HitRow(last, row, Offset.X + WidthForText, 0, out _, out _) + 1);
        }
        return (start, Math.Max(start, end));
    }

    private void DrawSearchMatch(DrawingContext context, LineLayout row, Point origin, int lineStart,
        (int Start, int Length) match, bool active)
    {
        bool dark = ActualThemeVariant == ThemeVariant.Dark;
        IBrush? fill = active ? null : dark ? DarkSearchFill : LightSearchFill;
        Pen? outline = active ? dark ? DarkSearchOutline : LightSearchOutline : null;
        DrawTextDecoration(context, row, origin, lineStart, match, fill, outline);
    }

    private void DrawTextDecoration(DrawingContext context, LineLayout row, Point origin, int lineStart,
        (int Start, int Length) match, IBrush? fill, Pen? outline)
    {
        int lineEnd = lineStart + row.Length;
        int matchEnd = match.Start + match.Length;
        if (match.Start > lineEnd || (match.Length == 0 ? matchEnd < lineStart : matchEnd <= lineStart)) return;

        int from = row.ToVisual(Math.Max(match.Start, lineStart) - lineStart);
        int to = row.ToVisual(Math.Min(matchEnd, lineEnd) - lineStart);
        if (from < to)
        {
            foreach (var rect in row.Layout.HitTestTextRange(from, to - from))
                context.DrawRectangle(fill, outline, rect.Translate(origin - default(Point)));
        }

        if (from == to || matchEnd > lineEnd)
        {
            var position = row.Layout.HitTestTextPosition(to);
            context.DrawRectangle(fill ?? outline?.Brush, null,
                new Rect(origin.X + position.X, origin.Y + position.Y, outline != null ? 2 : 3, LineHeight));
        }
    }
}
