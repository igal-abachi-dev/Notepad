using Avalonia.Headless.XUnit;
using Notepad.NeoEdit;
using NotepadAvalonia.Models;
using NotepadAvalonia.Services;
using System.Text.RegularExpressions;
using Xunit;

namespace Notepad.Tests;

public class SearchServiceTests
{
    private static NeoEditor CreateEditor(string text)
    {
        var editor = new NeoEditor();
        editor.Text = text;
        return editor;
    }

    [AvaloniaFact]
    public void FindNext_WrapsAroundWhenEnabled()
    {
        var editor = CreateEditor("abc abc");
        editor.CaretOffset = 5;

        var settings = new SearchSettings
        {
            SearchString = "abc",
            WrapAround = true
        };

        var service = new SearchService();
        var (result, wrapped) = service.FindNext(editor, settings);

        Assert.True(wrapped);
        Assert.Equal(new SearchResult(0, 3), result);
    }

    [AvaloniaFact]
    public void ReplaceAll_RespectsWholeWordOption()
    {
        var editor = CreateEditor("cat scatter cat");
        var settings = new SearchSettings
        {
            SearchString = "cat",
            ReplaceString = "dog",
            WholeWord = true
        };

        var service = new SearchService();
        var count = service.ReplaceAll(editor, settings);

        Assert.Equal(2, count);
        Assert.Equal("dog scatter dog", editor.GetText());
        editor.Undo();
        Assert.Equal("cat scatter cat", editor.GetText());
    }

    [AvaloniaFact]
    public void ReplaceAll_LiteralReplacement_PreservesDollarSign()
    {
        var editor = CreateEditor("cat CAT scatter");
        var settings = new SearchSettings
        {
            SearchString = "cat",
            ReplaceString = "$&",
            WholeWord = true
        };

        Assert.Equal(2, new SearchService().ReplaceAll(editor, settings));
        Assert.Equal("$& $& scatter", editor.GetText());
    }

    [AvaloniaFact]
    public void ReplaceAll_RegexReplacement_RespectsWholeWordAndCaptures()
    {
        var editor = CreateEditor("cat scatter cats");
        var settings = new SearchSettings
        {
            SearchString = "(cat)s?",
            ReplaceString = "<$1>",
            WholeWord = true,
            UseRegex = true
        };

        Assert.Equal(2, new SearchService().ReplaceAll(editor, settings));
        Assert.Equal("<cat> scatter <cat>", editor.GetText());
    }

    [AvaloniaFact]
    public void ReplaceNext_WithSelectedMatch_ReplacesSelection()
    {
        var editor = CreateEditor("cat cat");
        var service = new SearchService();
        var settings = new SearchSettings { SearchString = "cat", ReplaceString = "dog" };
        var result = Assert.IsType<SearchResult>(service.FindNext(editor, settings).result);
        editor.SetSelection(result.StartOffset, result.Length);

        Assert.Equal(1, service.ReplaceNext(editor, settings));
        Assert.Equal("dog cat", editor.GetText());
    }

    [AvaloniaFact]
    public void ReplaceNext_RegexMatch_ExpandsCapturesWithDocumentContext()
    {
        var editor = CreateEditor("cat! cat!");
        var service = new SearchService();
        var settings = new SearchSettings
        {
            SearchString = "(cat)(?=!)",
            ReplaceString = "<$1>",
            UseRegex = true
        };
        editor.SetSelection(0, 3);

        Assert.Equal(1, service.ReplaceNext(editor, settings));
        Assert.Equal("<cat>! cat!", editor.GetText());
    }

    [AvaloniaFact]
    public void FindPrevious_RegexSearch_CanFindMatchEndingAtCaret()
    {
        var editor = CreateEditor("dog cat");
        editor.CaretOffset = 7;
        var settings = new SearchSettings { SearchString = "cat", UseRegex = true };

        Assert.Equal(new SearchResult(4, 3), new SearchService().FindPrevious(editor, settings).result);
    }

    [AvaloniaFact]
    public void Search_InvalidRegex_ReportsErrorWithoutChangingText()
    {
        var editor = CreateEditor("cat");
        var service = new SearchService();
        var settings = new SearchSettings { SearchString = "[", UseRegex = true };

        Assert.Throws<RegexParseException>(() => service.FindNext(editor, settings));
        Assert.Throws<RegexParseException>(() => service.ReplaceNext(editor, settings));
        Assert.Throws<RegexParseException>(() => service.ReplaceAll(editor, settings));
        Assert.Equal("cat", editor.GetText());
    }

    [AvaloniaFact]
    public void ReplaceAll_SlowRegex_TimesOutWithoutChangingText()
    {
        string text = new string('a', 10_000) + "!";
        var editor = CreateEditor(text);
        var settings = new SearchSettings { SearchString = "^(a+)+$", UseRegex = true };

        Assert.Throws<RegexMatchTimeoutException>(() => new SearchService().ReplaceAll(editor, settings));
        Assert.Equal(text, editor.GetText());
    }
}
