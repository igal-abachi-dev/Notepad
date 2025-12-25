using System.Collections.Generic;
using System.Text;

namespace Notepad.NeoEdit.Backend;

public class StringBuffer
{
    // Optimization: StringBuilder prevents O(N^2) allocations during typing
    public StringBuilder Content { get; private set; }
    public List<int> LineStarts { get; private set; }

    public StringBuffer(string content)
    {
        Content = new StringBuilder(content);
        LineStarts = CreateLineStarts(content);
    }

    public void Append(string text)
    {
        int startOffset = Content.Length;
        Content.Append(text);
        var newLines = CreateLineStarts(text);
        // newLines[0] is always 0, so we skip it or handle offset
        // We append offsets relative to the start of the *chunk*, but in this simple 
        // append-only buffer, we track absolute offsets.
        for (int i = 1; i < newLines.Count; i++)
        {
            LineStarts.Add(newLines[i] + startOffset);
        }
    }

    private static List<int> CreateLineStarts(string str)
    {
        var r = new List<int>() { 0 };
        for (int i = 0; i < str.Length; i++)
        {
            char c = str[i];
            if (c == '\r')
            {
                if (i + 1 < str.Length && str[i + 1] == '\n') { r.Add(i + 2); i++; }
                else { r.Add(i + 1); }
            }
            else if (c == '\n') { r.Add(i + 1); }
        }
        return r;
    }
}