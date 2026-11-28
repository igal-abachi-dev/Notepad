using System;
using System.Text;

namespace Notepad.NeoEdit;

public sealed partial class NeoEditor
{
    private string GetIndentationAt(int offset)
    {
        var (line, column) = _doc.GetLineColumnFromOffset(offset);
        string text = _doc.GetLineContent(line) ?? "";
        return text[..GetIndentationLength(text.AsSpan(0, Math.Min(column, text.Length)))];
    }

    private static int GetIndentationLength(ReadOnlySpan<char> text)
    {
        int content = text.IndexOfAnyExcept(' ', '\t');
        return content < 0 ? text.Length : content;
    }

    private void AppendLineDelimiter(StringBuilder output, string delimiter, int replaceStart)
    {
        if (delimiter.Length == 0) return;
        if (delimiter[0] == '\n' && (output.Length > 0 ? output[^1] == '\r' :
            replaceStart > 0 && _doc.GetTextInRange(replaceStart - 1, 1) == "\r")) output.Append('\r');
        output.Append(delimiter);
    }

    internal void UpdateFollowedText(string text)
    {
        string previous = GetText();
        if (text == previous) return;
        if (text.StartsWith(previous, StringComparison.Ordinal))
        {
            ApplyChange(previous.Length, 0, text[previous.Length..]);
            _snapshot = text;
            Revision = _savedRevision = ++_nextRevision;
            _undo.Clear();
            _undoCharacters = 0;
            _redo.Clear();
            _continueGroup = false;
            _columns = null;
            _anchorOffset = _caretOffset = TextLength;
            PublishChange();
        }
        else
        {
            LoadDocument(text);
            CaretOffset = TextLength;
        }
    }

    private void InsertBlankLineBelow()
    {
        int line = _doc.GetLineColumnFromOffset(_caretOffset).Item1;
        int next = GetLineStartOffset(line + 1);
        if (line + 1 == LineCount)
            ReplaceRange(next, 0, NewLine, EditKind.Independent);
        else
        {
            string ending = SafeLineInsertion(next, NewLine);
            ReplaceRange(next, 0, ending, EditKind.Independent, endCaret: next);
        }
    }

    private string SafeLineInsertion(int offset, string ending)
    {
        // A lone CR followed immediately by LF (or vice versa at the previous
        // boundary) would fuse with an existing separator instead of adding a line.
        if (ending == "\r" && offset < TextLength && _doc.GetTextInRange(offset, 1) == "\n") return "\r\n";
        if (ending == "\n" && offset > 0 && _doc.GetTextInRange(offset - 1, 1) == "\r") return "\r\n";
        return ending;
    }

    private void ChangeLines(int direction, bool copy)
    {
        var (first, last) = GetSelectedLines();
        if (!copy && (direction < 0 && first == 0 || direction > 0 && last == LineCount - 1)) return;

        int startLine = copy || direction > 0 ? first : first - 1;
        int endLine = copy || direction < 0 ? last : last + 1;
        int start = GetLineStartOffset(startLine);
        int end = GetLineStartOffset(endLine + 1);
        int count = endLine - startLine + 1;
        var offsets = new int[count + (copy ? last - first + 2 : 1)];
        var output = new StringBuilder(end - start);
        int selectedCount = last - first + 1;
        int positionCount = copy ? count + selectedCount : count;
        int newFirst = copy ? (direction < 0 ? 0 : count) : (direction < 0 ? 0 : 1);
        for (int slot = 0; slot < positionCount; slot++)
        {
            offsets[slot] = start + output.Length;
            int sourceLine = copy ? first + slot % selectedCount :
                direction < 0 ? (slot == count - 1 ? startLine : first + slot) : (slot == 0 ? endLine : first + slot - 1);
            int source = GetLineStartOffset(sourceLine);
            output.Append(_doc.GetTextInRange(source, _doc.GetLineContentLength(sourceLine)));

            int delimiterLine = copy ? first + slot % selectedCount : startLine + slot;
            int delimiterStart = GetLineStartOffset(delimiterLine) + _doc.GetLineContentLength(delimiterLine);
            int delimiterLength = GetLineStartOffset(delimiterLine + 1) - delimiterStart;
            if (delimiterLength != 0) AppendLineDelimiter(output, _doc.GetTextInRange(delimiterStart, delimiterLength), start);
            else if (slot + 1 < positionCount) AppendLineDelimiter(output, NewLine, start);
        }
        offsets[positionCount] = start + output.Length;
        var selected = new Selection(Translate(_anchorOffset), Translate(_caretOffset));
        ReplaceRange(start, end - start, output.ToString(), EditKind.Independent, selection: selected);

        int Translate(int offset)
        {
            var (line, column) = _doc.GetLineColumnFromOffset(offset);
            return offsets[newFirst + line - first] + column;
        }

    }

    private (int First, int Last) GetSelectedLines()
    {
        int first = _doc.GetLineColumnFromOffset(SelectionStart).Item1;
        var (last, column) = _doc.GetLineColumnFromOffset(SelectionStart + SelectionLength);
        return (first, last > first && column == 0 ? last - 1 : last);
    }
}
