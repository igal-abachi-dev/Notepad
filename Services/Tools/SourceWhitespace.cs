using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace NotepadAvalonia.Services.Tools;

// Whitespace-only formatting. Existing JavaScript and TypeScript line terminators are kept
// so restricted productions such as return and postfix ++ keep their meaning.
internal static class SourceWhitespace
{
    internal static string Format(string source, bool css, bool indent, string newline, bool typeScript = false)
    {
        var output = new StringBuilder(source.Length);
        var delimiters = new Stack<char>();
        int position = 0, depth = 0, parentheses = 0, brackets = 0;
        char previous = '\0';
        int previousStart = 0, previousEnd = 0;
        while (position < source.Length)
        {
            int trivia = position;
            while (position < source.Length && IsWhitespace(source[position], css)) position++;
            if (position == source.Length)
            {
                if (!css && source.AsSpan(trivia).ContainsAny("\r\n\u2028\u2029"))
                    output.Append(source.AsSpan(trivia));
                break;
            }
            int start = position;
            char current = source[start];
            var gap = source.AsSpan(trivia, start - trivia);
            bool lineBreak = gap.ContainsAny("\r\n\u2028\u2029");
            bool insideGroup = parentheses != 0 || brackets != 0;
            var priorToken = source.AsSpan(previousStart, previousEnd - previousStart);
            bool syntax = ScanToken(source, ref position, css, priorToken, lineBreak, typeScript);
            char punctuation = syntax ? current : '\0';
            CheckDelimiter(delimiters, punctuation);
            if (punctuation == '}') depth--;

            bool structuralBreak = indent && previousEnd != 0 && !insideGroup &&
                (previous == '{' && punctuation != '}' || previous == ';' && punctuation != '}' ||
                 punctuation == '}' && previous != '{');
            if (lineBreak && !css)
            {
                // Keep the actual terminators and any comments on their original side.
                int lastBreak = gap.LastIndexOfAny("\r\n\u2028\u2029");
                output.Append(gap[..(lastBreak + 1)]);
                if (indent) output.Append(' ', depth * 2);
            }
            else if (structuralBreak || indent && lineBreak)
            {
                output.Append(newline).Append(' ', depth * 2);
            }
            else if (previousEnd != 0 && !gap.IsEmpty && (indent || insideGroup || !CanRemoveGap(previous, punctuation, css)))
            {
                output.Append(' ');
            }

            output.Append(source.AsSpan(start, position - start));
            if (syntax)
            {
                switch (punctuation)
                {
                    case '{': depth++; break;
                    case '(': parentheses++; break;
                    case ')': parentheses--; break;
                    case '[': brackets++; break;
                    case ']': brackets--; break;
                }
            }
            previous = punctuation;
            previousStart = start;
            previousEnd = position;
        }

        if (delimiters.Count != 0) throw new FormatException("Unclosed source delimiter.");
        return output.ToString();
    }

    private static void CheckDelimiter(Stack<char> delimiters, char punctuation)
    {
        switch (punctuation)
        {
            case '(':
                delimiters.Push(')');
                break;
            case '[':
                delimiters.Push(']');
                break;
            case '{':
                delimiters.Push('}');
                break;
            case ')':
            case ']':
            case '}':
                if (!delimiters.TryPop(out char expected) || punctuation != expected)
                    throw new FormatException("Mismatched source delimiter.");
                break;
        }
    }

    private static bool CanRemoveGap(char previous, char current, bool css) => css
        ? previous is '{' or '}' or ';' || current is '{' or '}' or ';'
        : previous is '{' or '(' or '[' or ',' or ';' || current is '}' or ')' or ']' or ',' or ';';

    private static bool ScanToken(string text, ref int position, bool css, ReadOnlySpan<char> previous,
        bool afterLineBreak, bool typeScript, int templateDepth = 0)
    {
        char current = text[position];
        if (current is '"' or '\'')
        {
            ReadQuoted(text, ref position, current, css);
            return false;
        }
        if (text.AsSpan(position).StartsWith("/*"))
        {
            ReadBlockComment(text, ref position);
            return false;
        }
        if (css)
        {
            if (text.AsSpan(position).StartsWith("--") && ReadCssCustomProperty(text, ref position)) return false;
            if (TryReadCssUrl(text, ref position)) return false;
            if (current is '(' or '[')
            {
                ReadCssGroup(text, ref position, current == '(' ? ')' : ']');
                return false;
            }
            if (current == '\\')
            {
                ReadCssEscape(text, ref position);
                return false;
            }
        }
        else
        {
            var rest = text.AsSpan(position);
            if (rest.StartsWith("//") || position == 0 && rest.StartsWith("#!") || rest.StartsWith("<!--") ||
                (previous.IsEmpty || afterLineBreak) && rest.StartsWith("-->"))
            {
                int length = rest.IndexOfAny("\r\n\u2028\u2029");
                position += length < 0 ? text.Length - position : length;
                return false;
            }
            if (current == '\u0060')
            {
                ReadTemplate(text, ref position, typeScript, templateDepth);
                return false;
            }
            if (current == '<' && position + 1 < text.Length &&
                (text[position + 1] is '/' or '>' || !typeScript && char.IsLetter(text[position + 1]) &&
                    (previous.IsEmpty || afterLineBreak || ExpressionStart(previous))))
                throw new FormatException("JSX is outside the formatter's supported syntax.");
            if (current == '/')
            {
                if (previous.IsEmpty || ExpressionStart(previous))
                {
                    ReadRegex(text, ref position);
                    return false;
                }
                if (position + 1 < text.Length && text[position + 1] == '>')
                    throw new FormatException("JSX is outside the formatter's supported syntax.");
                if (afterLineBreak || !EndsExpression(previous))
                    throw new FormatException("Cannot safely distinguish a JavaScript regular expression from division here.");
            }
        }

        position++;
        if (!IsWord(current) && !(css && current == '-')) return true;
        while (position < text.Length)
        {
            char next = text[position];
            if (css && next == '\\') ReadCssEscape(text, ref position);
            else if (IsWord(next) || css && next == '-') position++;
            else break;
        }
        return false;
    }

    private static bool ExpressionStart(ReadOnlySpan<char> previous) => previous is
        "=" or "(" or "[" or "{" or "," or ":" or ";" or "!" or "?" or ">" or "return" or "throw" or "case" or "yield";

    private static bool EndsExpression(ReadOnlySpan<char> previous) => previous.Length > 0 &&
        (IsWord(previous[0]) || previous[0] is '\'' or '"' or '\u0060' || previous is "]");

    private static bool IsWord(char character) => char.IsLetterOrDigit(character) || character is '_' or '$' ||
        character > 127 && !IsWhitespace(character, css: false);

    private static bool IsWhitespace(char character, bool css) =>
        css ? character is ' ' or '\t' or '\f' or '\r' or '\n' :
            character is '\t' or '\v' or '\f' or '\r' or '\n' or '\u2028' or '\u2029' or '\uFEFF' ||
            char.GetUnicodeCategory(character) == UnicodeCategory.SpaceSeparator;

    private static void ReadQuoted(string text, ref int position, char quote, bool css)
    {
        for (position++; position < text.Length;)
        {
            char current = text[position++];
            if (current == quote) return;
            if (current == '\\' && position < text.Length)
            {
                if (css)
                {
                    position--;
                    ReadCssEscape(text, ref position);
                }
                else
                {
                    if (text[position++] == '\r' && position < text.Length && text[position] == '\n') position++;
                }
            }
            else if (current is '\r' or '\n')
                throw new FormatException("Unterminated string.");
        }
        throw new FormatException("Unterminated string.");
    }

    private static void ReadTemplate(string text, ref int position, bool typeScript, int depth)
    {
        if (depth >= 64) throw new FormatException("JavaScript template nesting exceeds 64 levels.");
        for (position++; position < text.Length;)
        {
            char character = text[position++];
            if (character == '\u0060') return;
            if (character == '\\' && position < text.Length) position++;
            else if (character == '$' && position < text.Length && text[position] == '{')
                ReadTemplateExpression(text, ref position, typeScript, depth + 1);
        }
        throw new FormatException("Unterminated JavaScript template literal.");
    }

    private static void ReadTemplateExpression(string text, ref int position, bool typeScript, int depth)
    {
        int braces = 1;
        int previousStart = position, previousEnd = ++position;
        while (position < text.Length)
        {
            int trivia = position;
            while (position < text.Length && IsWhitespace(text[position], css: false)) position++;
            if (position == text.Length) break;
            int start = position;
            var previous = text.AsSpan(previousStart, previousEnd - previousStart);
            bool afterLineBreak = text.AsSpan(trivia, position - trivia).ContainsAny("\r\n\u2028\u2029");
            if (ScanToken(text, ref position, css: false, previous, afterLineBreak, typeScript, depth))
            {
                if (text[start] == '{') braces++;
                if (text[start] == '}' && --braces == 0) return;
            }
            previousStart = start;
            previousEnd = position;
        }
        throw new FormatException("Unterminated JavaScript template expression.");
    }

    private static void ReadBlockComment(string text, ref int position)
    {
        int end = text.IndexOf("*/", position + 2, StringComparison.Ordinal);
        if (end < 0) throw new FormatException("Unterminated block comment.");
        position = end + 2;
    }

    private static void ReadRegex(string text, ref int position)
    {
        bool characterClass = false;
        for (position++; position < text.Length;)
        {
            char character = text[position++];
            if (character is '\r' or '\n' or '\u2028' or '\u2029') break;
            if (character == '\\' && position < text.Length)
            {
                if (text[position++] is '\r' or '\n' or '\u2028' or '\u2029') break;
            }
            else if (character == '[') characterClass = true;
            else if (character == ']') characterClass = false;
            else if (character == '/' && !characterClass)
            {
                while (position < text.Length && IsWord(text[position])) position++;
                return;
            }
        }
        throw new FormatException("Unterminated JavaScript regular expression.");
    }

    private static void ReadCssGroup(string text, ref int position, char closing)
    {
        position++;
        var delimiters = new Stack<char>();
        delimiters.Push(closing);
        while (position < text.Length)
        {
            char current = text[position];
            if (current is '\'' or '"') ReadQuoted(text, ref position, current, css: true);
            else if (text.AsSpan(position).StartsWith("/*")) ReadBlockComment(text, ref position);
            else if (!ReadCssNameOrUrl(text, ref position))
            {
                position++;
                CheckDelimiter(delimiters, current);
                if (delimiters.Count == 0) return;
            }
        }
        throw new FormatException("Unterminated CSS group.");
    }

    private static bool ReadCssNameOrUrl(string text, ref int position)
    {
        if (TryReadCssUrl(text, ref position)) return true;
        int start = position;
        while (position < text.Length)
        {
            char current = text[position];
            if (current == '\\') ReadCssEscape(text, ref position);
            else if (IsWord(current) || current == '-') position++;
            else break;
        }
        return position != start;
    }

    private static bool TryReadCssUrl(string text, ref int position)
    {
        if (!text.AsSpan(position).StartsWith("url(", StringComparison.OrdinalIgnoreCase)) return false;
        int value = position + 4;
        while (value < text.Length && IsWhitespace(text[value], css: true)) value++;
        if (value < text.Length && text[value] is '\'' or '"') return false;
        position = value;
        // Inside an unquoted URL, slash-star is data, including in data URLs.
        while (position < text.Length && text[position] != ')' && !IsWhitespace(text[position], css: true))
        {
            if (text[position] == '\\') ReadCssEscape(text, ref position);
            else if (text[position] is '(' or '\'' or '"') throw new FormatException("Invalid unquoted CSS URL.");
            else position++;
        }
        while (position < text.Length && IsWhitespace(text[position], css: true)) position++;
        if (position == text.Length || text[position++] != ')') throw new FormatException("Unterminated CSS URL.");
        return true;
    }

    private static bool ReadCssCustomProperty(string text, ref int position)
    {
        int scan = position + 2;
        while (scan < text.Length)
        {
            if (text[scan] == '\\') ReadCssEscape(text, ref scan);
            else if (IsWord(text[scan]) || text[scan] == '-') scan++;
            else break;
        }
        while (scan < text.Length && IsWhitespace(text[scan], css: true)) scan++;
        if (scan == text.Length || text[scan] != ':') return false;

        int braces = 0;
        for (scan++; scan < text.Length;)
        {
            char current = text[scan];
            if (braces == 0 && current is ';' or '}') break;
            if (current is '\'' or '"') ReadQuoted(text, ref scan, current, css: true);
            else if (current is '(' or '[') ReadCssGroup(text, ref scan, current == '(' ? ')' : ']');
            else if (text.AsSpan(scan).StartsWith("/*")) ReadBlockComment(text, ref scan);
            else if (!ReadCssNameOrUrl(text, ref scan))
            {
                if (current == '{') braces++;
                if (current == '}') braces--;
                scan++;
            }
        }
        if (braces != 0) throw new FormatException("Unterminated CSS custom property.");
        position = scan;
        return true;
    }
    private static void ReadCssEscape(string text, ref int position)
    {
        int start = ++position;
        while (position < text.Length && position - start < 6 && char.IsAsciiHexDigit(text[position])) position++;
        if (position < text.Length && (position == start || IsWhitespace(text[position], css: true)) &&
            text[position++] == '\r' && position < text.Length && text[position] == '\n') position++;
    }
}
