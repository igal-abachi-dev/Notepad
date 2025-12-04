using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NotepadAvalonia.Models;

namespace NotepadAvalonia.Services;
/// <summary>
/// Print service - cross-platform printing abstraction.
/// Produces paginated text chunks with header/footer; actual printing is platform-specific.
/// </summary>
public class PrintService
{
    public IReadOnlyList<string> Paginate(string content, DocumentModel document, PageSetupSettings setup, int charsPerLine = 80, int linesPerPage = 50)
    {
        if (linesPerPage < 3) throw new ArgumentOutOfRangeException(nameof(linesPerPage), "linesPerPage must allow header/footer.");
        if (charsPerLine < 10) throw new ArgumentOutOfRangeException(nameof(charsPerLine), "charsPerLine too small.");

        var normalized = content.Replace("\r\n", "\n").Replace("\r", "\n");
        var wrappedLines = WrapLines(normalized.Split('\n'), charsPerLine).ToList();

        var pages = new List<string>();
        int usableLines = linesPerPage - 2; // header + footer
        int totalPages = (int)Math.Ceiling((double)Math.Max(1, wrappedLines.Count) / Math.Max(1, usableLines));

        if (wrappedLines.Count == 0)
        {
            wrappedLines.Add(string.Empty);
        }

        for (int pageIndex = 0; pageIndex < totalPages; pageIndex++)
        {
            var pageLines = new List<string>();
            var header = FormatHeaderFooter(setup.Header, document, pageIndex + 1);
            var footer = FormatHeaderFooter(setup.Footer, document, pageIndex + 1);
            pageLines.Add(header);

            int start = pageIndex * usableLines;
            var slice = wrappedLines.Skip(start).Take(usableLines);
            pageLines.AddRange(slice);

            // pad to footer position
            while (pageLines.Count < linesPerPage - 1)
            {
                pageLines.Add(string.Empty);
            }

            pageLines.Add(footer);
            pages.Add(string.Join("\n", pageLines));
        }

        return pages;
    }

    private IEnumerable<string> WrapLines(IEnumerable<string> lines, int maxChars)
    {
        foreach (var line in lines)
        {
            if (line.Length <= maxChars)
            {
                yield return line;
                continue;
            }

            int index = 0;
            while (index < line.Length)
            {
                int take = Math.Min(maxChars, line.Length - index);
                yield return line.Substring(index, take);
                index += take;
            }
        }
    }

    private string FormatHeaderFooter(string template, DocumentModel document, int pageNumber)
    {
        var sb = new StringBuilder(template ?? string.Empty);
        sb.Replace("&f", document.FileName);
        sb.Replace("&p", pageNumber.ToString());
        sb.Replace("&d", DateTime.Now.ToShortDateString());
        sb.Replace("&t", DateTime.Now.ToShortTimeString());
        return sb.ToString();
    }
}
