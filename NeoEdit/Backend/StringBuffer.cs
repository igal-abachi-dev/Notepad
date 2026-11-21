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
        LineStarts = new List<int> { 0 };
        AddLineStarts(content, 0);
    }

    public void Append(string text)
    {
        int startOffset = Content.Length;
        Content.Append(text);
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
