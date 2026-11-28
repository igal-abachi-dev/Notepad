using System;
using System.Globalization;
using System.Text;

namespace NotepadAvalonia.Services.Tools;

internal static class TextCasing
{
    internal static string ToCamelCase(string text, TextInfo casing)
    {
        var output = new StringBuilder(text.Length);
        int word = -1;
        bool previousUpper = false;
        for (int offset = 0; offset < text.Length;)
        {
            int start = offset;
            if (!Rune.TryGetRuneAt(text, offset, out var rune)) return text;
            offset += rune.Utf16SequenceLength;
            var category = Rune.GetUnicodeCategory(rune);
            bool mark = category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;
            if (!Rune.IsLetterOrDigit(rune) && !mark)
            {
                AppendWord(start);
                word = -1;
                continue;
            }

            bool upper = Rune.IsUpper(rune) || category == UnicodeCategory.TitlecaseLetter;
            if (word >= 0 && upper && (!previousUpper ||
                Rune.TryGetRuneAt(text, offset, out var next) && Rune.IsLower(next)))
            {
                AppendWord(start);
                word = start;
            }
            if (word < 0) word = start;
            if (!mark) previousUpper = upper;
        }
        AppendWord(text.Length);
        return output.Length == 0 ? text : output.ToString();

        void AppendWord(int end)
        {
            if (word < 0 || word == end) return;
            string part = casing.ToLower(text[word..end]);
            if (output.Length == 0) output.Append(part);
            else
            {
                int first = StringInfo.GetNextTextElementLength(part.AsSpan());
                output.Append(casing.ToUpper(part[..first])).Append(part.AsSpan(first));
            }
        }
    }
}
