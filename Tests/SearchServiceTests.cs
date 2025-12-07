using AvaloniaEdit;
using NotepadAvalonia.Models;
using NotepadAvalonia.Services;
using Xunit;

namespace Notepad.Tests;

public class SearchServiceTests
{
    private static TextEditor CreateEditor(string text)
    {
        var editor = new TextEditor();
        editor.Document.Text = text;
        return editor;
    }

    [Fact]
    public void FindNext_WrapsAroundWhenEnabled()
    {
        var editor = CreateEditor("abc abc");
        editor.SelectionStart = 5; // after second space

        var settings = new SearchSettings
        {
            SearchString = "abc",
            WrapAround = true
        };

        var service = new SearchService();
        var result = service.FindNext(editor, settings);

        Assert.NotNull(result);
        Assert.Equal(0, result!.result.StartOffset);
        Assert.Equal(3, result.result.Length);
    }

    [Fact]
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
        Assert.Equal("dog scatter dog", editor.Document.Text);
    }
}
