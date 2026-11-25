using Avalonia.Headless.XUnit;
using Notepad.NeoEdit;
using NotepadAvalonia.Models;
using NotepadAvalonia.Services;
using System.Globalization;
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
    public void LiteralSearch_UsesTheDocumentSpanForCanonicallyEquivalentMatches()
    {
        var editor = CreateEditor("cafe\u0301 caf\u00e9");
        var service = new SearchService();
        var settings = new SearchSettings { SearchString = "caf\u00e9", ReplaceString = "x", MatchCase = true, WholeWord = true };

        Assert.Equal(new SearchResult(0, 5), service.FindNext(editor, settings).result);
        editor.CaretOffset = editor.TextLength;
        Assert.Equal(new SearchResult(6, 4), service.FindPrevious(editor, settings).result);
        editor.SetSelection(0, 5);
        Assert.Equal(1, service.ReplaceNext(editor, settings));
        Assert.Equal("x caf\u00e9", editor.Text);
        Assert.Equal(1, service.ReplaceAll(editor, settings));
        Assert.Equal("x x", editor.Text);
        editor.Undo();
        Assert.Equal("x caf\u00e9", editor.Text);
        editor.Undo();
        Assert.Equal("cafe\u0301 caf\u00e9", editor.Text);
    }

    [AvaloniaFact]
    public void LiteralSearch_UsesLocaleCasingAndKeepsExplicitControlCharacterQueries()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            var editor = CreateEditor("I i \u0131 \u0130 a\0b");
            var service = new SearchService();
            Assert.Equal(new SearchResult(2, 1), service.FindNext(editor, new() { SearchString = "i" }).result);
            Assert.Equal(new SearchResult(9, 1), service.FindNext(editor, new() { SearchString = "\0" }).result);
            Assert.Null(service.FindNext(editor, new() { SearchString = "ab" }).result);
            Assert.Equal(1, service.ReplaceAll(editor, new() { SearchString = "\0", ReplaceString = "-" }));
            Assert.Equal("I i \u0131 \u0130 a-b", editor.Text);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [AvaloniaFact]
    public void ReplaceAll_ZeroWidthRegexCannotSplitCrLfOrASurrogatePair()
    {
        var editor = CreateEditor("a\r\n\U0001F600b");
        var service = new SearchService();
        var settings = new SearchSettings { SearchString = "(?=)", ReplaceString = "|", UseRegex = true };

        Assert.Equal(5, service.ReplaceAll(editor, settings));
        Assert.Equal("|a|\r\n|\U0001F600|b|", editor.Text);
        editor.Undo();
        Assert.Equal("a\r\n\U0001F600b", editor.Text);
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
    public void Find_WrapUsesDisjointWindowsAndDoesNotMarkMissesAsWrapped()
    {
        var editor = CreateEditor("aba");
        editor.CaretOffset = 1;
        var settings = new SearchSettings { SearchString = "aba", WrapAround = true };
        var service = new SearchService();
        Assert.Equal((null, false), service.FindNext(editor, settings));
        Assert.Equal((null, false), service.FindPrevious(editor, settings));
    }

    [AvaloniaFact]
    public void FindPrevious_OverlappingCandidates_AreEvaluatedFromTheRight()
    {
        var editor = CreateEditor("a-a-ab");
        editor.CaretOffset = 6;
        var settings = new SearchSettings { SearchString = "a-a", WholeWord = true };
        var service = new SearchService();
        Assert.Equal(new SearchResult(0, 3), service.FindPrevious(editor, settings).result);

        editor.LoadDocument("ababa");
        editor.CaretOffset = 5;
        settings = new() { SearchString = "aba", UseRegex = true };
        Assert.Equal(new SearchResult(2, 3), service.FindPrevious(editor, settings).result);
    }

    [AvaloniaFact]
    public void FindNext_ZeroWidthAdvancesAndCanWrapToADifferentPosition()
    {
        var editor = CreateEditor("a a");
        var settings = new SearchSettings { SearchString = "(?=a)", UseRegex = true, WrapAround = true };
        var service = new SearchService();
        var first = service.FindNext(editor, settings);
        Assert.Equal((new SearchResult(0, 0), false), first);
        editor.SetSelection(first.result!.StartOffset, 0);
        var second = service.FindNext(editor, settings);
        Assert.Equal((new SearchResult(2, 0), false), second);
        editor.SetSelection(second.result!.StartOffset, 0);
        Assert.Equal((new SearchResult(0, 0), true), service.FindNext(editor, settings));

        settings.SearchString = "$";
        Assert.Equal(new SearchResult(3, 0), service.FindNext(editor, settings).result);
        editor.CaretOffset = 3;
        Assert.Equal((null, false), service.FindNext(editor, settings));
    }

    [AvaloniaFact]
    public void ReplaceNext_SelectsBeforeReplacingAndSelectsTheFollowingMatch()
    {
        var editor = CreateEditor("one one");
        var settings = new SearchSettings { SearchString = "one", ReplaceString = "two" };
        var service = new SearchService();

        Assert.Equal(0, service.ReplaceNext(editor, settings));
        Assert.Equal("one one", editor.GetText());
        Assert.Equal((0, 3), (editor.SelectionStart, editor.SelectionLength));
        Assert.Equal(1, service.ReplaceNext(editor, settings));
        Assert.Equal("two one", editor.GetText());
        Assert.Equal((4, 3), (editor.SelectionStart, editor.SelectionLength));
        editor.Undo();
        Assert.Equal((0, 3), (editor.SelectionStart, editor.SelectionLength));
        Assert.Equal("one one", editor.GetText());
    }

    [AvaloniaFact]
    public void ReplaceAll_MapsCaretAndCannotSplitASurrogatePair()
    {
        var editor = CreateEditor("ab");
        editor.CaretOffset = 1;
        var service = new SearchService();
        Assert.Equal(1, service.ReplaceAll(editor, new() { SearchString = "ab", ReplaceString = "\U0001F600" }));
        Assert.Equal(2, editor.CaretOffset);
        Assert.Equal(0, service.ReplaceAll(editor, new() { SearchString = ".", UseRegex = true }));
        Assert.Equal("\U0001F600", editor.GetText());
        editor.Undo();
        Assert.Equal("ab", editor.GetText());
        Assert.Equal(1, editor.CaretOffset);
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
