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
        var doc = new DocumentModel { FilePath = @"C:\test\file.txt", IsUntitled = false };
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

        var text = string.Join("\n", Enumerable.Repeat("0123456789", 30)); // 30 lines
        var pages = service.Paginate(text, doc, setup, charsPerLine: 5, linesPerPage: 6);

        Assert.True(pages.Count > 1);
    }
}
