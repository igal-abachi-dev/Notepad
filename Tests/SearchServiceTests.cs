using Avalonia.Headless.XUnit;
using Notepad.NeoEdit;
using NotepadAvalonia.Models;
using NotepadAvalonia.Services;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using Xunit;

namespace Notepad.Tests;

public class SearchServiceTests
{
    [AvaloniaFact]
    public void RegexFindReplace_MatchesMixedLineEndingsAndExpandsReplacementEscapes()
    {
        var editor = new NeoEditor { Text = "end\r\nstart end\nstart end\rstart" };
        var settings = new SearchSettings { SearchString = @"(end)(?:\r\n|\n|\r)(start)", ReplaceString = @"$2\r\n$1", UseRegex = true };
        var service = new SearchService();
        var (first, _) = service.FindNext(editor, settings);
        Assert.NotNull(first);
        Assert.Equal(10, first.Length);
        Assert.Equal(3, service.ReplaceAll(editor, settings));
        Assert.Equal("start\r\nend start\r\nend start\r\nend", editor.Text);
        editor.Undo();
        Assert.Equal("end\r\nstart end\nstart end\rstart", editor.Text);
    }

    [Fact]
    public void PreviewMatches_UsesOriginalRegexContextAndStopsAtViewportEnd()
    {
        const string text = "key:word key:word key:word";
        var settings = new SearchSettings { SearchString = "(?<=key:)word", UseRegex = true };
        var matches = SearchService.FindPreviewMatches(text, settings, 13, 17, 100, CancellationToken.None);
        Assert.Equal(new[] { (113, 4) }, matches);

        settings.SearchString = "(?=word)";
        Assert.Equal(new[] { (113, 0) }, SearchService.FindPreviewMatches(text, settings, 13, 17, 100, CancellationToken.None));
        settings.SearchString = "(";
        Assert.Empty(SearchService.FindPreviewMatches(text, settings, 13, 17, 0, CancellationToken.None));
    }

    [Fact]
    public void PreviewMatches_PreservesUnicodeMatchLengthAndWholeWord()
    {
        const string text = "caf\u00e9 cafe\u0301 cafe\u0301ine";
        var settings = new SearchSettings { SearchString = "caf\u00e9", WholeWord = true };
        Assert.Equal(new[] { (5, 5) }, SearchService.FindPreviewMatches(text, settings, 7, text.Length, 0, CancellationToken.None));
    }

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

    [AvaloniaTheory]
    [InlineData("\u034F")]
    [InlineData("\uFE0F")]
    [InlineData("\U000E0001")]
    [InlineData("\U000E0100")]
    public void LiteralSearch_PreservesUnrequestedInvisibleTextAndFindsExplicitQueries(string mark)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            string protectedWord = "a" + mark + "b";
            var editor = CreateEditor("ab " + protectedWord + " ab");
            int lastWord = 4 + protectedWord.Length;
            var service = new SearchService();
            var settings = new SearchSettings { SearchString = "ab", ReplaceString = "x", MatchCase = true };

            editor.CaretOffset = 2;
            Assert.Equal(new SearchResult(lastWord, 2), service.FindNext(editor, settings).result);
            editor.CaretOffset = lastWord;
            Assert.Equal(new SearchResult(0, 2), service.FindPrevious(editor, settings).result);

            editor.SetSelection(3, protectedWord.Length);
            Assert.Equal(0, service.ReplaceNext(editor, settings));
            Assert.Equal(2, service.ReplaceAll(editor, settings));
            Assert.Equal("x " + protectedWord + " x", editor.Text);
            editor.Undo();
            Assert.Equal("ab " + protectedWord + " ab", editor.Text);

            editor.CaretOffset = 0;
            settings.SearchString = protectedWord;
            Assert.Equal(new SearchResult(3, protectedWord.Length), service.FindNext(editor, settings).result);
            settings.SearchString = mark;
            Assert.Equal(new SearchResult(4, mark.Length), service.FindNext(editor, settings).result);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [AvaloniaFact]
    public void LiteralSearch_DoesNotLeaveAnUnrequestedVariationSelectorBehind()
    {
        var editor = CreateEditor("\u2764\uFE0F \u2764");
        var service = new SearchService();
        var settings = new SearchSettings { SearchString = "\u2764", ReplaceString = "x", MatchCase = true };

        Assert.Equal(new SearchResult(3, 1), service.FindNext(editor, settings).result);
        editor.CaretOffset = 2;
        Assert.Null(service.FindPrevious(editor, settings).result);
        Assert.Equal(1, service.ReplaceAll(editor, settings));
        Assert.Equal("\u2764\uFE0F x", editor.Text);

        editor.CaretOffset = 0;
        settings.SearchString = "\u2764\uFE0F";
        Assert.Equal(new SearchResult(0, 2), service.FindNext(editor, settings).result);
    }

    [AvaloniaTheory]
    [InlineData("cs")]
    [InlineData("dzs")]
    public void FindPrevious_HungarianContractionSelectsTheFullMatch(string word)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("hu-HU");
            var editor = CreateEditor(word + " " + word);
            editor.CaretOffset = editor.TextLength;
            var service = new SearchService();
            var settings = new SearchSettings { SearchString = word, ReplaceString = "x", WholeWord = true };

            var found = Assert.IsType<SearchResult>(service.FindPrevious(editor, settings).result);
            Assert.Equal(new SearchResult(word.Length + 1, word.Length), found);
            editor.SetSelection(found.StartOffset, found.Length);
            Assert.Equal(1, service.ReplaceNext(editor, settings));
            Assert.Equal(word + " x", editor.Text);
            editor.Undo();
            Assert.Equal(word, editor.GetSelectedText());
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

    [AvaloniaFact]
    public void ReplaceNext_SlowFollowUpSearch_PreservesTheCompletedReplacement()
    {
        string suffix = " " + new string('a', 10_000) + "!";
        var editor = CreateEditor("ok" + suffix);
        editor.SetSelection(0, 2);
        var service = new SearchService();
        var settings = new SearchSettings { SearchString = "ok|(?<= )(a+)+$", ReplaceString = "x", UseRegex = true };

        Assert.Equal(1, service.ReplaceNext(editor, settings, out bool found, out bool wrapped, out bool followUpTimedOut));
        Assert.True(followUpTimedOut);
        Assert.False(found);
        Assert.False(wrapped);
        Assert.Equal("x" + suffix, editor.Text);
        editor.Undo();
        Assert.Equal("ok" + suffix, editor.Text);
    }
}
