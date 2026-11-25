using NotepadAvalonia.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;

namespace NotepadAvalonia.Services;

public class PrintService
{
    [SupportedOSPlatform("windows")]
    internal Task<bool> PrintAsync(nint owner, string filename, string content, PageSetupSettings setup,
        EditorSettings font, bool direct) => WindowsPrinting.PrintAsync(owner, filename, content, setup, font, direct);

    internal sealed record Page(int Number, AlignedText Header, IReadOnlyList<string> Body, AlignedText Footer);
    public sealed record AlignedText(string Left, string Center, string Right)
    {
        internal string AsText() => string.Concat(Left, Center, Right);
    }

    public IReadOnlyList<string> Paginate(string content, DocumentModel document, PageSetupSettings setup,
        int charsPerLine = 80, int linesPerPage = 50)
    {
        if (charsPerLine < 1) throw new ArgumentOutOfRangeException(nameof(charsPerLine));
        return BuildPages(content, document.FileName, setup, linesPerPage, text => Math.Min(charsPerLine, text.Length), DateTime.Now)
            .Select(page => FormatPage(page, setup, linesPerPage)).ToList();
    }

    private static string FormatPage(Page page, PageSetupSettings settings, int linesPerPage)
    {
        var rows = new List<string>();
        if (HasHeader(settings)) { rows.Add(page.Header.AsText()); rows.Add(""); }
        rows.AddRange(page.Body);
        if (HasFooter(settings))
        {
            while (rows.Count < linesPerPage - 1) rows.Add("");
            rows.Add(page.Footer.AsText());
        }
        return string.Join("\n", rows);
    }

    internal static bool HasHeader(PageSetupSettings setup) => !string.IsNullOrEmpty(setup.Header);
    internal static bool HasFooter(PageSetupSettings setup) => !string.IsNullOrEmpty(setup.Footer);

    internal static IEnumerable<Page> BuildPages(string content, string filename, PageSetupSettings setup,
        int linesPerPage, Func<string, int> fit, DateTime moment)
    {
        int capacity = linesPerPage - (HasHeader(setup) ? 2 : 0) - (HasFooter(setup) ? 2 : 0);
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(linesPerPage), "The page has no room for text.");
        List<string> body = [];
        int number = 1;
        foreach (var row in Wrap(content, fit))
        {
            body.Add(row);
            if (body.Count != capacity) continue;
            yield return MakePage(number++, body);
            body = [];
        }
        if (body.Count != 0) yield return MakePage(number, body);

        Page MakePage(int page, List<string> rows) => new(page,
            ExpandPageText(setup.Header, filename, page, moment),
            rows,
            ExpandPageText(setup.Footer, filename, page, moment));
    }

    private static IEnumerable<string> Wrap(string text, Func<string, int> fit)
    {
        int start = 0;
        do
        {
            int lineEnd = text.AsSpan(start).IndexOfAny('\r', '\n');
            lineEnd = lineEnd < 0 ? text.Length : start + lineEnd;
            string line = ExpandTabs(text.AsSpan(start, lineEnd - start));
            int offset = 0;
            do
            {
                if (offset == line.Length)
                {
                    yield return "";
                    break;
                }

                string remaining = line[offset..];
                int limit = Math.Clamp(fit(remaining), 0, remaining.Length);
                if (limit == remaining.Length)
                {
                    yield return remaining;
                    break;
                }

                int boundary = 0;
                int word = 0;
                while (boundary < limit)
                {
                    int element = StringInfo.GetNextTextElementLength(remaining.AsSpan(boundary));
                    if (boundary + element > limit) break;
                    boundary += element;
                    if (remaining[boundary - 1] == ' ') word = boundary;
                }
                int take = word > 0 ? word : boundary > 0 ? boundary : StringInfo.GetNextTextElementLength(remaining);
                yield return remaining[..take];
                offset += take;
            } while (offset < line.Length);

            start = lineEnd + 1;
            if (lineEnd < text.Length - 1 && text[lineEnd] == '\r' && text[lineEnd + 1] == '\n') start++;
        } while (start < text.Length);
    }

    private static string ExpandTabs(ReadOnlySpan<char> line)
    {
        if (!line.Contains('\t')) return line.ToString();
        var expanded = new StringBuilder(line.Length);
        foreach (char c in line)
        {
            if (c == '\t') expanded.Append(' ', 4 - expanded.Length % 4);
            else expanded.Append(c);
        }
        return expanded.ToString();
    }

    public static AlignedText ExpandPageText(string? template, string filename, int page, DateTime moment)
    {
        var slots = new[] { new StringBuilder(), new StringBuilder(), new StringBuilder() };
        int align = 1;
        if (template != null)
        {
            for (int i = 0; i < template.Length; i++)
            {
                char value = template[i];
                if (value != '&' || i == template.Length - 1)
                {
                    slots[align].Append(value);
                    continue;
                }
                char code = template[++i];
                switch (char.ToLowerInvariant(code))
                {
                    case 'l': align = 0; break;
                    case 'c': align = 1; break;
                    case 'r': align = 2; break;
                    case 'f': slots[align].Append(filename); break;
                    case 'p': slots[align].Append(page.ToString(CultureInfo.CurrentCulture)); break;
                    case 'd': slots[align].Append(moment.ToLongDateString()); break;
                    case 't': slots[align].Append(moment.ToLongTimeString()); break;
                    default: slots[align].Append(code); break;
                }
            }
        }
        return new(slots[0].ToString(), slots[1].ToString(), slots[2].ToString());
    }
}
