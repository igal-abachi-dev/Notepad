using System.IO;
using System.Linq;
using System;
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
        var pages = service.Paginate(text, doc, setup, charsPerLine: 10, linesPerPage: 8);

        Assert.Equal(2, pages.Count);
        Assert.Contains("0123456789\nabcdefghij", pages[0]);
    }

    [Fact]
    public void ExpandPageText_UsesAlignmentAndNeverReparsesSubstitutions()
    {
        var fields = PrintService.ExpandPageText("&L&&p &f&C&P&Rright&", "a&p.txt", 1, DateTime.Now);
        Assert.Equal(new PrintService.AlignedText("&p a&p.txt", "1", "right&"), fields);
    }

    [Theory]
    [InlineData("\r\n")]
    [InlineData("\n")]
    [InlineData("\r")]
    public void Paginate_TerminalSeparatorDoesNotStartAnEmptyPage(string ending)
    {
        var document = new DocumentModel();
        var pageSetup = new PageSetupSettings { Header = "", Footer = "" };
        var content = string.Concat(Enumerable.Repeat("x" + ending, 48));
        var page = Assert.Single(new PrintService().Paginate(content, document, pageSetup, 10, 48));
        Assert.Equal(48, page.Split('\n').Length);
    }

    [Fact]
    public void Paginate_AvoidsBreakingSurrogatesAndCombiningSequences()
    {
        var pages = new PrintService().Paginate("123456789\U0001F600Ze\u0301", new(),
            new() { Header = "", Footer = "" }, 10, 5);
        Assert.Equal("123456789\n\U0001F600Ze\u0301", Assert.Single(pages));
    }
}
