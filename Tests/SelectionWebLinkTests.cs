using System;
using System.Globalization;
using System.Linq;
using NotepadAvalonia.Services.Tools;
using Xunit;

namespace Notepad.Tests;

public class SelectionWebLinkTests
{
    private const string Selected = "café + code&mode=1 # fragment\r\nnext line";

    [Fact]
    public void SearchAndDefine_EncodeSelectionAsOneGoogleQuery()
    {
        var search = SelectionWebLink.Create(SelectionWebAction.Search, Selected, CultureInfo.InvariantCulture)!;
        Assert.Equal("https", search.Scheme);
        Assert.Equal("www.google.com", search.Host);
        Assert.Equal("/search", search.AbsolutePath);
        Assert.Empty(search.Fragment);
        Assert.Equal(Selected, ReadParameter(search, "q"));
        Assert.Single(search.Query.Split('&'));

        var define = SelectionWebLink.Create(SelectionWebAction.Define, "  idempotent  ", CultureInfo.InvariantCulture)!;
        Assert.Equal("define:idempotent", ReadParameter(define, "q"));
    }

    [Theory]
    [InlineData("Explain", "Explain this error")]
    [InlineData("Summarize", "Summarize the selected text")]
    [InlineData("Rewrite", "Rewrite the selected text")]
    public void AiMode_IncludesTheRequestedPromptAndExactSelection(string action, string opening)
    {
        var uri = SelectionWebLink.Create(Enum.Parse<SelectionWebAction>(action), Selected, CultureInfo.InvariantCulture)!;
        Assert.Equal("www.google.com", uri.Host);
        Assert.Equal("50", ReadParameter(uri, "udm"));
        string query = ReadParameter(uri, "q");
        Assert.StartsWith(opening, query);
        Assert.EndsWith("\n\nSelected text:\n" + Selected, query);
    }

    [Theory]
    [InlineData("es-MX", "es")]
    [InlineData("zh-Hant", "zh-TW")]
    [InlineData("", "en")]
    public void Translate_AutoDetectsSourceAndUsesTheUiLanguage(string culture, string language)
    {
        var uri = SelectionWebLink.Create(SelectionWebAction.Translate, Selected, CultureInfo.GetCultureInfo(culture))!;
        Assert.Equal("translate.google.com", uri.Host);
        Assert.Equal("auto", ReadParameter(uri, "sl"));
        Assert.Equal(language, ReadParameter(uri, "tl"));
        Assert.Equal(Selected, ReadParameter(uri, "text"));
    }

    [Fact]
    public void Create_RejectsWhitespaceAndOverlongInputWithoutTruncating()
    {
        var culture = CultureInfo.InvariantCulture;
        Assert.Null(SelectionWebLink.Create(SelectionWebAction.Search, " \r\n\t", culture));
        Assert.Null(SelectionWebLink.Create(SelectionWebAction.Search, new string('a', 2049), culture));
        Assert.Null(SelectionWebLink.Create(SelectionWebAction.Explain, new string('\u6587', 1024), culture));
        Assert.NotNull(SelectionWebLink.Create(SelectionWebAction.Explain, new string('a', 2048), culture));
    }

    private static string ReadParameter(Uri uri, string name) => Uri.UnescapeDataString(uri.Query[1..].Split('&')
        .Single(part => part.StartsWith(name + "=", StringComparison.Ordinal))[(name.Length + 1)..]);
}
