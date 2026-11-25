using System;
using System.Collections.Generic;
using System.Text;

namespace Notepad.NeoEdit.Backend;

internal sealed class StringBuffer
{
    private readonly string? _original;
    private readonly StringBuilder? _added;
    public List<int> LineStarts { get; } = [0];
    public int Length => _original?.Length ?? _added!.Length;
    public char this[int offset] => _original != null ? _original[offset] : _added![offset];

    public StringBuffer() => _added = new StringBuilder();

    public StringBuffer(string content)
    {
        _original = content;
        AddLineStarts(content, 0);
    }

    public void CopyTo(int offset, Span<char> destination)
    {
        if (_original != null) _original.AsSpan(offset, destination.Length).CopyTo(destination);
        else _added!.CopyTo(offset, destination, destination.Length);
    }

    public void Append(string text)
    {
        if (_added == null) throw new InvalidOperationException("The original buffer is read-only.");
        int startOffset = _added.Length;
        _added.Append(text);
        AddLineStarts(text, startOffset);
    }

    private void AddLineStarts(string text, int startOffset)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n') i++;
                LineStarts.Add(startOffset + i + 1);
            }
            else if (text[i] == '\n')
            {
                LineStarts.Add(startOffset + i + 1);
            }
        }
    }
}
