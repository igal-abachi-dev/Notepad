using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using System;
using System.Globalization;
using System.Text;

namespace Notepad.NeoEdit;

public sealed partial class NeoEditor
{
    private readonly record struct ColumnPoint(int Line, int Column);
    private readonly record struct ColumnSelection(ColumnPoint Anchor, ColumnPoint Active)
    {
        public int First => Math.Min(Anchor.Line, Active.Line);
        public int Last => Math.Max(Anchor.Line, Active.Line);
        public int Left => Math.Min(Anchor.Column, Active.Column);
        public int Right => Math.Max(Anchor.Column, Active.Column);
    }

    private ColumnSelection? _columns;
    private bool _columnDrag;
    internal bool HasColumnSelection => _columns.HasValue;
    internal bool HasTextSelection => SelectionLength != 0 || _columns is { } block && block.Right > block.Left;
    private bool CanSelectColumns => !WordWrap && !RightToLeftReadingOrder;

    private ColumnPoint GetColumnPoint(int offset)
    {
        var (line, column) = _doc.GetLineColumnFromOffset(offset);
        string text = _doc.GetLineContent(line) ?? "";
        return new(line, MeasureColumns(text.AsSpan(0, Math.Min(column, text.Length)), 0));
    }

    private static int MeasureColumns(ReadOnlySpan<char> text, int column)
    {
        while (!text.IsEmpty)
        {
            column += text[0] == '\t' ? TabSize - column % TabSize : 1;
            text = text[StringInfo.GetNextTextElementLength(text)..];
        }
        return column;
    }

    private static (int Offset, int Column) ColumnBoundary(ReadOnlySpan<char> text, int target, bool roundUp = false)
    {
        int offset = 0, column = 0;
        while (offset < text.Length && column < target)
        {
            int width = text[offset] == '\t' ? TabSize - column % TabSize : 1;
            if (!roundUp && width > target - column) break;
            offset += StringInfo.GetNextTextElementLength(text[offset..]);
            column += width;
        }
        return (offset, column);
    }

    private void SelectColumns(ColumnSelection block)
    {
        if (_columns != block) SelectionVersion++;
        _columns = block;
        int line = block.Active.Line;
        int local = ColumnBoundary(_doc.GetLineContent(line), block.Active.Column).Offset;
        int caret = GetLineStartOffset(line) + local;
        MoveSelection(caret, caret, preserveColumns: true);
    }

    private bool HandleColumnAndLineKeys(Key key, bool shift, bool alt, bool primary, bool control)
    {
        if (primary || control) return false;
        if (key == Key.Enter && shift && !alt && !IsReadOnly)
        {
            InsertBlankLineBelow();
            return true;
        }
        if (!alt) return false;
        if (shift && CanSelectColumns && (key is Key.Left or Key.Right || _columns.HasValue && key is Key.Up or Key.Down))
        {
            var point = GetColumnPoint(_caretOffset);
            var block = _columns ?? new ColumnSelection(point, point);
            var active = block.Active;
            int line = Math.Clamp(active.Line + (key == Key.Up ? -1 : key == Key.Down ? 1 : 0), 0, LineCount - 1);
            int column = Math.Max(0, active.Column + (key == Key.Left ? -1 : key == Key.Right ? 1 : 0));
            SelectColumns(block with { Active = new(line, column) });
            return true;
        }
        if (key is not (Key.Up or Key.Down) || IsReadOnly) return false;
        ChangeLines(key == Key.Up ? -1 : 1, copy: shift);
        return true;
    }

    private string CopyColumns(ColumnSelection block)
    {
        var output = new StringBuilder();
        for (int line = block.First; line <= block.Last; line++)
        {
            if (line != block.First) output.Append(NewLine);
            var row = _doc.GetLineContent(line).AsSpan();
            int start = ColumnBoundary(row, block.Left).Offset;
            int end = ColumnBoundary(row, block.Right, roundUp: true).Offset;
            output.Append(row[start..end]);
        }
        return output.ToString();
    }

    private bool TryEditColumns(string text, int deleteDirection = 0)
    {
        if (_columns is not { } block) return false;
        int left = block.Left, right = block.Right;
        if (left == right && deleteDirection < 0) left = Math.Max(0, left - 1);
        if (left == right && deleteDirection > 0) right++;

        string[]? pieces = null;
        if (text.AsSpan().ContainsAny('\r', '\n'))
        {
            pieces = TextLineEndings.Normalize(text, "\n").Split('\n');
            int rows = block.Last - block.First + 1;
            if (pieces.Length != rows && !(pieces.Length == rows + 1 && pieces[^1].Length == 0)) return true;
        }

        int start = GetLineStartOffset(block.First);
        var output = new StringBuilder();
        int caret = start;
        for (int line = block.First; line <= block.Last; line++)
        {
            string row = _doc.GetLineContent(line) ?? "";
            var front = ColumnBoundary(row, left);
            int end = left == right ? front.Offset : ColumnBoundary(row, right, roundUp: true).Offset;
            string inserted = pieces == null ? text : pieces[line - block.First];
            output.Append(row.AsSpan(0, front.Offset));
            if (inserted.Length != 0 && front.Column < left && front.Offset == row.Length)
                output.Append(' ', left - front.Column);
            output.Append(inserted);
            if (line == block.Active.Line) caret = start + output.Length;
            output.Append(row.AsSpan(end));
            int delimiterStart = GetLineStartOffset(line) + row.Length;
            AppendLineDelimiter(output, _doc.GetTextInRange(delimiterStart, GetLineStartOffset(line + 1) - delimiterStart), start);
        }

        int column = MeasureColumns(text, left);
        ColumnSelection? after = pieces == null ? new ColumnSelection(block.Anchor with { Column = column }, block.Active with { Column = column }) : null;
        ReplaceRange(start, GetLineStartOffset(block.Last + 1) - start, output.ToString(), EditKind.Independent,
            selection: new Selection(caret, caret, after));
        return true;
    }

    private void DrawColumnSelection(DrawingContext context, LineLayout row, Point origin, int line, IBrush fill)
    {
        if (_columns is not { } block || line < block.First || line > block.Last) return;
        var text = _doc.GetLineContent(line).AsSpan();
        var from = ColumnBoundary(text, block.Left);
        int start = row.ToVisual(from.Offset);
        if (block.Left == block.Right)
        {
            var hit = row.Layout.HitTestTextPosition(start);
            double x = origin.X + hit.X + (block.Left - from.Column) * FontSize * 0.6;
            context.FillRectangle(Foreground, new Rect(x, origin.Y + hit.Y, 1, LineHeight));
            return;
        }
        int end = row.ToVisual(ColumnBoundary(text, block.Right, roundUp: true).Offset);
        foreach (var rect in row.Layout.HitTestTextRange(start, end - start))
            context.FillRectangle(fill, rect.Translate(origin - default(Point)));
    }
}
