using System;
using System.Buffers;
using System.Text;

namespace NotepadAvalonia.Services.Tools;

internal static class CharacterInspector
{
    internal static string Describe(string sample, bool selection, bool truncated)
    {
        var output = new StringBuilder();
        var text = sample.AsSpan();
        int count = 0;
        while (count < 8 && Rune.DecodeFromUtf16(text, out var rune, out int consumed) == OperationStatus.Done)
        {
            if (count != 0) output.AppendLine();
            output.Append($"U+{rune.Value:X4}  {Label(rune)}");
            string? entity = HtmlEntity(rune.Value);
            if (entity != null) output.Append("  ·  ").Append(entity);
            count++;
            if (!selection) break;
            text = text[consumed..];
        }
        if (count == 0) return "No character at the caret.";
        if (selection && (!text.IsEmpty || truncated)) output.AppendLine().Append("…");
        return output.ToString();
    }

    private static string Label(Rune rune) => rune.Value switch
    {
        0 => "NUL",
        9 => "Tab",
        10 => "Line feed",
        13 => "Carriage return",
        32 => "Space",
        0xA0 => "Non-breaking space",
        0x200B => "Zero-width space",
        0x200C => "Zero-width non-joiner",
        0x200D => "Zero-width joiner",
        0x202F => "Narrow no-break space",
        0xFEFF => "Byte order mark / zero-width no-break space",
        _ => Rune.GetUnicodeCategory(rune).ToString()
    };

    private static string? HtmlEntity(int value) => value switch
    {
        0xA0 => "&nbsp;",
        0x200B => "&ZeroWidthSpace;",
        0x200C => "&zwnj;",
        0x200D => "&zwj;",
        '&' => "&amp;",
        '<' => "&lt;",
        '>' => "&gt;",
        '"' => "&quot;",
        '\'' => "&apos;",
        < 32 and not (9 or 10 or 13) or >= 0x7F and <= 0x9F => null,
        _ => $"&#x{value:X};"
    };
}
