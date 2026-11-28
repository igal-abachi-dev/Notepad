using System;
using System.Collections.Generic;
using System.Text;

namespace NotepadAvalonia.Services.Tools;

// Change only markup syntax whitespace. Spaces between elements can be visible text.
internal static class MarkupWhitespace
{
    internal static string Format(string text, bool html, bool indent, string newline)
    {
        var output = new StringBuilder(text.Length);
        var attributes = new List<(Range Name, Range? Value)>();
        int position = 0;
        while (position < text.Length)
        {
            int start = text.IndexOf('<', position);
            if (start < 0) break;
            output.Append(text.AsSpan(position, start - position));
            position = start + 1;
            var tail = text.AsSpan(start);
            if (tail.StartsWith("<!--"))
                position = After(text, start + 4, "-->");
            else if (tail.StartsWith("<![CDATA["))
                position = After(text, start + 9, "]]>");
            else if (tail.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase))
                position = ReadDeclaration(text, start + 9);
            else if (tail.StartsWith("<?") || tail.StartsWith("<!"))
                position = After(text, position + 1, !html && tail.StartsWith("<?") ? "?>" : ">");
            else
            {
                bool closing = position < text.Length && text[position] == '/';
                int nameStart = position + (closing ? 1 : 0);
                if (nameStart < text.Length && IsNameStart(text[nameStart], html))
                {
                    position = nameStart;
                    while (position < text.Length && !IsSpace(text[position]) && text[position] is not ('/' or '>'))
                    {
                        if (text[position] is '<' or '"' or '\'' or '=') throw new FormatException("Invalid markup tag name.");
                        position++;
                    }
                    var name = text.AsSpan(nameStart, position - nameStart);
                    WriteTag(text, start, ref position, closing, attributes, output, indent, newline);
                    if (html && !closing && IsRawElement(name))
                    {
                        int end = name.Equals("plaintext", StringComparison.OrdinalIgnoreCase)
                            ? text.Length : FindRawEnd(text, position, name);
                        // A legacy escaped script can contain an apparent closing tag.
                        if (name.Equals("script", StringComparison.OrdinalIgnoreCase) &&
                            text.AsSpan(position, end - position).Contains("<!--", StringComparison.Ordinal))
                            end = text.Length;
                        output.Append(text.AsSpan(position, end - position));
                        position = end;
                    }
                    continue;
                }
            }
            output.Append(text.AsSpan(start, position - start));
        }
        output.Append(text.AsSpan(position));
        return output.ToString();
    }

    private static void WriteTag(string text, int start, ref int position, bool closing,
        List<(Range Name, Range? Value)> attributes, StringBuilder output, bool indent, string newline)
    {
        int nameEnd = position;
        attributes.Clear();
        bool selfClosing = false;
        while (true)
        {
            int separator = position;
            SkipSpace(text, ref position);
            if (position == text.Length) throw new FormatException("Unterminated markup tag.");
            if (text[position] == '>') { position++; break; }
            if (!closing && text.AsSpan(position).StartsWith("/>"))
            {
                position += 2;
                selfClosing = true;
                break;
            }
            if (closing || separator == position) throw new FormatException("Invalid markup tag or missing attribute separator.");

            int attributeStart = position;
            while (position < text.Length && !IsSpace(text[position]) && text[position] is not ('=' or '>' or '/'))
            {
                if (text[position] is '<' or '"' or '\'') throw new FormatException("Invalid markup attribute name.");
                position++;
            }
            if (attributeStart == position) throw new FormatException("Expected a markup attribute name.");
            int attributeEnd = position;
            SkipSpace(text, ref position);
            Range? value = null;
            if (position < text.Length && text[position] == '=')
            {
                position++;
                SkipSpace(text, ref position);
                value = ReadAttributeValue(text, ref position);
            }
            else
            {
                position = attributeEnd;
            }
            attributes.Add((attributeStart..attributeEnd, value));
        }

        output.Append(text.AsSpan(start, nameEnd - start));
        bool multiline = indent && (attributes.Count > 1 || attributes.Count == 1 &&
            text.AsSpan(nameEnd, position - nameEnd).IndexOfAny('\r', '\n') >= 0);
        int indentStart = start;
        if (multiline)
        {
            while (indentStart > 0 && text[indentStart - 1] is ' ' or '\t') indentStart--;
            if (indentStart > 0 && text[indentStart - 1] is not ('\r' or '\n')) indentStart = start;
        }

        foreach (var (attribute, value) in attributes)
        {
            if (multiline) output.Append(newline).Append(text.AsSpan(indentStart, start - indentStart)).Append("  ");
            else output.Append(' ');
            output.Append(text.AsSpan()[attribute]);
            if (value is { } range) output.Append('=').Append(text.AsSpan()[range]);
        }
        if (multiline) output.Append(newline).Append(text.AsSpan(indentStart, start - indentStart));
        // An unquoted HTML value consumes a slash unless whitespace separates it from "/>".
        bool unquotedValue = attributes.Count != 0 && attributes[^1].Value is { } lastValue &&
            text.AsSpan()[lastValue][0] is not ('"' or '\'');
        if (selfClosing && !multiline && (indent || unquotedValue)) output.Append(' ');
        output.Append(selfClosing ? "/>" : ">");
    }

    private static Range ReadAttributeValue(string text, ref int position)
    {
        if (position == text.Length) throw new FormatException("Expected a markup attribute value.");
        int start = position;
        if (text[position] is '"' or '\'')
        {
            int end = text.IndexOf(text[position], position + 1);
            if (end < 0) throw new FormatException("Unterminated markup attribute value.");
            position = end + 1;
        }
        else
        {
            while (position < text.Length && !IsSpace(text[position]) && text[position] != '>')
            {
                if (text[position] is '<' or '"' or '\'' or '=' or '\u0060')
                    throw new FormatException("Invalid unquoted HTML attribute value.");
                position++;
            }
            if (start == position) throw new FormatException("Expected a markup attribute value.");
        }
        return start..position;
    }

    private static int FindRawEnd(string text, int position, ReadOnlySpan<char> name)
    {
        while (position < text.Length)
        {
            int start = text.IndexOf("</", position, StringComparison.Ordinal);
            if (start < 0) break;
            int afterName = start + 2 + name.Length;
            if (afterName < text.Length && text.AsSpan(start + 2, name.Length).Equals(name, StringComparison.OrdinalIgnoreCase) &&
                (IsSpace(text[afterName]) || text[afterName] is '/' or '>'))
                return start;
            position = start + 2;
        }
        return text.Length;
    }

    private static bool IsRawElement(ReadOnlySpan<char> name) =>
        name.Equals("script", StringComparison.OrdinalIgnoreCase) || name.Equals("style", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("textarea", StringComparison.OrdinalIgnoreCase) || name.Equals("title", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("iframe", StringComparison.OrdinalIgnoreCase) || name.Equals("xmp", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("noembed", StringComparison.OrdinalIgnoreCase) || name.Equals("noframes", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("noscript", StringComparison.OrdinalIgnoreCase) || name.Equals("plaintext", StringComparison.OrdinalIgnoreCase);

    private static int ReadDeclaration(string text, int position)
    {
        while (position < text.Length)
        {
            char current = text[position++];
            if (current == '>') return position;
            if (current is '"' or '\'')
            {
                int end = text.IndexOf(current, position);
                if (end < 0) break;
                position = end + 1;
            }
        }
        return text.Length;
    }

    private static int After(string text, int position, string marker)
    {
        int index = text.IndexOf(marker, position, StringComparison.Ordinal);
        return index < 0 ? text.Length : index + marker.Length;
    }

    private static void SkipSpace(string text, ref int position)
    {
        while (position < text.Length && IsSpace(text[position])) position++;
    }

    private static bool IsSpace(char value) => value is ' ' or '\t' or '\n' or '\r' or '\f';
    private static bool IsNameStart(char value, bool html) => char.IsAsciiLetter(value) || !html && (value is '_' or ':' || value >= 0x80);
}
