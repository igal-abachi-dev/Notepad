using System.IO;
using System.Linq;
using NotepadAvalonia.Models;
using NotepadAvalonia.Services;
using Xunit;

namespace Notepad.Tests;

public class PrintServiceTests
{
    [Fact]
    public void Paginate_IncludesHeaderFooterTokens()
    {
        var service = new PrintService();
        var doc = new DocumentModel { FilePath = Path.Combine("test", "file.txt"), IsUntitled = false };
        var setup = new PageSetupSettings
        {
            Header = "&f - &p",
            Footer = "Printed &d"
        };

        var pages = service.Paginate("hello", doc, setup, charsPerLine: 20, linesPerPage: 5);

        Assert.Single(pages);
        Assert.Contains("file.txt - 1", pages[0]);
        Assert.Contains("Printed", pages[0]);
    }

    [Fact]
    public void Paginate_WrapsLongLinesAndCreatesMultiplePages()
    {
        var service = new PrintService();
        var doc = new DocumentModel();
        var setup = new PageSetupSettings();

        var text = string.Join("\n", Enumerable.Repeat("0123456789abcdefghij", 3));
        var pages = service.Paginate(text, doc, setup, charsPerLine: 10, linesPerPage: 6);

        Assert.Equal(2, pages.Count);
        Assert.Contains("0123456789\nabcdefghij", pages[0]);
    }
}
