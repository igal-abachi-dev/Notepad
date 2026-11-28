using Avalonia;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Notepad.NeoEdit;

/// <summary>Colors for a zero-based document line. The gutter color is used when line numbers are visible.</summary>
public readonly record struct EditorLineDecoration(int Line, IBrush? Background = null, IBrush? GutterBackground = null);

/// <summary>A background for a UTF-16 range in this pane's text, including a single character within a line.</summary>
/// <remarks>
/// For a current-line detail pane below a document pane, load the line as its own text. Clip each
/// range to the line and subtract the line's original start offset to get the detail-pane offset.
/// </remarks>
public readonly record struct EditorInlineDecoration(int StartOffset, int Length, IBrush Background);

/// <summary>Blank visual rows before a zero-based line. Use LineCount to place them after the document.</summary>
public readonly record struct EditorLineSpacer(int BeforeLine, int RowCount);

public sealed partial class NeoEditor
{
    private Dictionary<int, EditorLineDecoration>? _lineDecorations;
    private (EditorInlineDecoration Decoration, int MaxEnd)[]? _inlineDecorations;
    private (int BeforeLine, int AddedRows)[]? _lineSpacers;
    private bool HasAlignedRows => !WordWrap && _lineSpacers != null;

    /// <summary>Replaces visual decorations for the current document revision. An edit clears them.</summary>
    public void SetDecorations(IEnumerable<EditorLineDecoration>? lines = null,
        IEnumerable<EditorInlineDecoration>? inline = null)
    {
        VerifyAccess();
        var lineColors = new Dictionary<int, EditorLineDecoration>();
        if (lines != null)
        {
            foreach (var decoration in lines)
            {
                if (decoration.Line < 0 || decoration.Line >= LineCount)
                    throw new ArgumentOutOfRangeException(nameof(lines), "The decorated line must exist.");
                lineColors[decoration.Line] = decoration;
            }
        }

        var ranges = inline?.OrderBy(range => range.StartOffset).ToArray() ?? [];
        var indexed = new (EditorInlineDecoration Decoration, int MaxEnd)[ranges.Length];
        int maxEnd = 0;
        for (int i = 0; i < ranges.Length; i++)
        {
            var range = ranges[i];
            if (range.StartOffset < 0 || range.Length <= 0 || range.Length > TextLength - range.StartOffset)
                throw new ArgumentOutOfRangeException(nameof(inline), "The decorated range must be inside the document.");
            ArgumentNullException.ThrowIfNull(range.Background, nameof(inline));
            maxEnd = Math.Max(maxEnd, range.StartOffset + range.Length);
            indexed[i] = (range, maxEnd);
        }
        _lineDecorations = lineColors.Count == 0 ? null : lineColors;
        _inlineDecorations = indexed.Length == 0 ? null : indexed;
        InvalidateVisual();
    }

    /// <summary>Replaces spacer rows for the current revision. Alignment is active with WordWrap off.</summary>
    /// <remarks>See <see cref="Offset"/> for synchronizing the document panes. An edit clears the spacers.</remarks>
    public void SetLineSpacers(IEnumerable<EditorLineSpacer>? spacers = null)
    {
        VerifyAccess();
        var rows = new SortedDictionary<int, int>();
        if (spacers != null)
        {
            foreach (var spacer in spacers)
            {
                if (spacer.BeforeLine < 0 || spacer.BeforeLine > LineCount || spacer.RowCount <= 0)
                    throw new ArgumentOutOfRangeException(nameof(spacers), "A spacer needs a valid line boundary and a positive row count.");
                rows[spacer.BeforeLine] = checked(rows.GetValueOrDefault(spacer.BeforeLine) + spacer.RowCount);
            }
        }

        var indexed = new (int BeforeLine, int AddedRows)[rows.Count];
        int total = 0;
        int index = 0;
        foreach (var (line, count) in rows)
        {
            total = checked(total + count);
            if (total > int.MaxValue - LineCount)
                throw new ArgumentOutOfRangeException(nameof(spacers), "There are too many display rows.");
            indexed[index++] = (line, total);
        }
        _lineSpacers = indexed.Length == 0 ? null : indexed;
        UpdateScrollInfo();
        _inputMethod.NotifyGeometry();
        InvalidateVisual();
    }

    /// <summary>Gets the vertical content coordinate of a document line, including visual spacers.</summary>
    public double GetLineTop(int line)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(line);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(line, LineCount);
        return TopOfLine(line);
    }

    /// <summary>Gets the UTF-16 start offset of a document line. LineCount denotes the end of the document.</summary>
    public int GetLineStartOffset(int line)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(line);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(line, LineCount);
        return line == LineCount ? TextLength : _doc.GetOffsetFromLineColumn(line, 0);
    }

    private void ClearDisplayAnnotations()
    {
        _lineDecorations = null;
        _inlineDecorations = null;
        _lineSpacers = null;
    }

    private int SpacerRowsBefore(int line)
    {
        if (_lineSpacers is not { } spacers) return 0;
        int low = 0;
        int high = spacers.Length;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (spacers[middle].BeforeLine <= line) low = middle + 1;
            else high = middle;
        }
        return low == 0 ? 0 : spacers[low - 1].AddedRows;
    }

    private void DrawLineDecorations(DrawingContext context, int first, int last)
    {
        if (_lineDecorations is not { } decorations) return;
        for (int line = first; line <= last; line++)
        {
            if (!decorations.TryGetValue(line, out var decoration)) continue;
            double y = TopOfLine(line) - Offset.Y;
            double height = WordWrap ? GetLineLayout(line).Layout.Height : LineHeight;
            if (decoration.Background is { } fill)
                context.FillRectangle(fill, new Rect(TextLeft, y, Math.Max(0, Bounds.Width - TextLeft), height));
            if (ShowLineNumbers && decoration.GutterBackground is { } gutter)
                context.FillRectangle(gutter, new Rect(0, y, TextLeft, height));
        }
    }

    private void DrawInlineDecorations(DrawingContext context, LineLayout row, Point origin, int lineStart)
    {
        if (_inlineDecorations is not { } ranges) return;
        int low = 0;
        int high = ranges.Length;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (ranges[middle].MaxEnd <= lineStart) low = middle + 1;
            else high = middle;
        }
        for (int i = low; i < ranges.Length; i++)
        {
            var range = ranges[i].Decoration;
            if (range.StartOffset > lineStart + row.Length) break;
            DrawTextDecoration(context, row, origin, lineStart, (range.StartOffset, range.Length), range.Background, null);
        }
    }
}
