using System;
using System.Text;

namespace Notepad.NeoEdit;

public sealed partial class NeoEditor
{
    internal void ToggleLineComments(string marker)
    {
        if (IsReadOnly || HasColumnSelection) return;
        var (first, last) = GetSelectedLines();
        int start = GetLineStartOffset(first);
        int end = GetLineStartOffset(last + 1);
        var rows = new (string Text, int Indent, bool Commented)[last - first + 1];
        bool hasText = false, allCommented = true;
        bool batch = marker == "REM";
        for (int i = 0; i < rows.Length; i++)
        {
            string row = _doc.GetLineContent(first + i) ?? "";
            int indent = GetIndentationLength(row);
            var content = row.AsSpan(indent);
            bool commented = content.StartsWith(marker, batch ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) &&
                (!batch || content.Length == marker.Length || char.IsWhiteSpace(content[marker.Length]));
            rows[i] = (row, indent, commented);
            hasText |= !content.IsEmpty;
            allCommented &= content.IsEmpty || commented;
        }
        bool remove = hasText && allCommented;
        string prefix = marker + " ";
        var output = new StringBuilder(end - start);
        var points = new (int Start, int At, int Removed, int Added)[rows.Length];
        for (int i = 0; i < rows.Length; i++)
        {
            var (row, indent, _) = rows[i];
            int from = output.Length;
            int removed = 0, added = 0;
            output.Append(row.AsSpan(0, indent));
            if (row.Length > indent || rows.Length == 1)
            {
                if (remove)
                {
                    removed = marker.Length;
                    if (row.Length > indent + removed && row[indent + removed] == ' ') removed++;
                }
                else
                {
                    output.Append(prefix);
                    added = prefix.Length;
                }
            }
            output.Append(row.AsSpan(indent + removed));
            int line = first + i;
            int delimiter = GetLineStartOffset(line) + row.Length;
            AppendLineDelimiter(output, _doc.GetTextInRange(delimiter, GetLineStartOffset(line + 1) - delimiter), start);
            points[i] = (from, indent, removed, added);
        }

        ReplaceRange(start, end - start, output.ToString(), EditKind.Independent,
            selection: new Selection(Translate(_anchorOffset), Translate(_caretOffset)));

        int Translate(int offset)
        {
            var (line, column) = _doc.GetLineColumnFromOffset(offset);
            if (line > last) return start + output.Length + column;
            var (lineStart, at, removed, added) = points[line - first];
            int local = column < at ? column : Math.Max(at, column - removed) + added;
            return start + lineStart + local;
        }
    }

    internal bool ToggleBlockComment(string opening, string closing, bool useWholeLines)
    {
        if (IsReadOnly || HasColumnSelection) return false;
        bool selected = SelectionLength != 0;
        int start = SelectionStart;
        int end = start + SelectionLength;
        if (useWholeLines || !selected)
        {
            var (first, last) = GetSelectedLines();
            var firstLine = _doc.GetLineContent(first) ?? "";
            start = GetLineStartOffset(first) + GetIndentationLength(firstLine);
            end = GetLineStartOffset(last) + _doc.GetLineContentLength(last);
        }

        string text = _doc.GetTextInRange(start, end - start);
        bool remove = text.Length >= opening.Length + closing.Length && text.StartsWith(opening, StringComparison.Ordinal) &&
            text.EndsWith(closing, StringComparison.Ordinal);
        if (!remove && (text.Contains(closing, StringComparison.Ordinal) ||
            opening == "<!--" && (text.Contains("--", StringComparison.Ordinal) || text.EndsWith('-')))) return false;

        string replacement = remove ? text[opening.Length..^closing.Length] : opening + text + closing;
        int caret = start + Math.Clamp(_caretOffset - start + (remove ? -opening.Length : opening.Length), 0, replacement.Length);
        int replacementEnd = start + replacement.Length;
        var after = selected ? _anchorOffset <= _caretOffset ? new Selection(start, replacementEnd) : new Selection(replacementEnd, start)
            : new Selection(caret, caret);
        ReplaceRange(start, end - start, replacement, EditKind.Independent, selection: after);
        return true;
    }

}
