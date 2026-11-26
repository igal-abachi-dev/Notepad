using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Input.TextInput;
using Notepad.NeoEdit;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Notepad.Tests;

public class NeoEditorTests
{
    [AvaloniaFact]
    public void Overtype_ReplacesGraphemesWithoutEatingTheNewline_AndUndoRestoresThem()
    {
        var editor = new NeoEditor();
        var window = Show(editor);
        try
        {
            editor.LoadDocument("a\u0301\U0001f600\r\nnext");
            Press(window, Key.Insert, PhysicalKey.Insert);
            Assert.True(editor.OvertypeMode);
            window.KeyTextInput("A");
            window.KeyTextInput("B");
            window.KeyTextInput("C");
            Assert.Equal("ABC\r\nnext", editor.Text);
            editor.Undo();
            Assert.Equal("a\u0301\U0001f600\r\nnext", editor.Text);
            editor.Redo();
            Assert.Equal("ABC\r\nnext", editor.Text);
            Press(window, Key.Insert, PhysicalKey.Insert);
            Assert.False(editor.OvertypeMode);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ConvertLineEndings_MapsReverseSelectionAndRestoresItOnUndo()
    {
        var editor = new NeoEditor { Text = "a\r\nb\nc\rd" };
        editor.SetSelection(7, -4);
        editor.ConvertLineEndings("\n");
        Assert.Equal("a\nb\nc\nd", editor.Text);
        Assert.Equal((6, 2, "b\nc\n"), (editor.SelectionAnchor, editor.CaretOffset, editor.GetSelectedText()));
        Assert.Equal("\n", editor.NewLine);
        editor.Undo();
        Assert.Equal("a\r\nb\nc\rd", editor.Text);
        Assert.Equal((7, 3, "\r\n"), (editor.SelectionAnchor, editor.CaretOffset, editor.NewLine));
        Assert.False(editor.IsModified);
        editor.Redo();
        Assert.Equal("\n", editor.NewLine);
    }

    [AvaloniaFact]
    public void ControlBackspace_SeparatesPunctuationFromWords()
    {
        var editor = new NeoEditor();
        var window = Show(editor);
        try
        {
            editor.LoadDocument("foo.bar.baz()");
            editor.CaretOffset = editor.TextLength;
            window.KeyPress(Key.Back, RawInputModifiers.Control, PhysicalKey.Backspace, null);
            Assert.Equal("foo.bar.baz", editor.Text);
            window.KeyPress(Key.Back, RawInputModifiers.Control, PhysicalKey.Backspace, null);
            Assert.Equal("foo.bar.", editor.Text);
            window.KeyPress(Key.Back, RawInputModifiers.Control, PhysicalKey.Backspace, null);
            Assert.Equal("foo.bar", editor.Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void LinkDetection_OnlyReturnsWebUrlsAndExcludesWrappingPunctuation()
    {
        var editor = new NeoEditor { Text = "see (https://example.com/a_(b)).\r\nfile:///C:/test" };
        Assert.Equal("https://example.com/a_(b)", editor.GetWebLinkAt(17)?.AbsoluteUri);
        Assert.Null(editor.GetWebLinkAt(1));
        Assert.Null(editor.GetWebLinkAt(editor.TextLength));
    }

    [AvaloniaFact]
    public void Backspace_AfterAnEditJoinsCrLf_RemovesTheEntireSeparator()
    {
        var editor = new NeoEditor();
        var window = Show(editor);
        try
        {
            editor.LoadDocument("a\nb");
            editor.ReplaceRange(1, 0, "\r");
            Assert.Equal(3, editor.CaretOffset);
            Assert.Equal(2, editor.LineCount);
            Press(window, Key.Back, PhysicalKey.Backspace);
            Assert.Equal("ab", editor.Text);
            editor.Undo();
            Assert.Equal("a\r\nb", editor.Text);
            Assert.Equal(3, editor.CaretOffset);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Navigation_ClampsSelectionAndCollapsesEitherOrientationToTheRightEdge()
    {
        var editor = new NeoEditor();
        var window = Show(editor);
        try
        {
            editor.LoadDocument("abcdefg\r\n\uD83D\uDE00");
            foreach (bool reverse in new[] { false, true })
            {
                editor.SetSelection(reverse ? 5 : 2, reverse ? -3 : 3);
                Press(window, Key.Left, PhysicalKey.ArrowLeft);
                Assert.Equal(2, editor.CaretOffset);
                Assert.Equal(0, editor.SelectionLength);
                editor.SetSelection(reverse ? 5 : 2, reverse ? -3 : 3);
                Press(window, Key.Right, PhysicalKey.ArrowRight);
                Assert.Equal(5, editor.CaretOffset);
                Assert.Equal(0, editor.SelectionLength);
            }
            Press(window, Key.End, PhysicalKey.End, RawInputModifiers.Control);
            Assert.Equal(editor.Text.Length, editor.CaretOffset);
            Press(window, Key.Home, PhysicalKey.Home, RawInputModifiers.Control);
            Assert.Equal(0, editor.CaretOffset);
            editor.CaretOffset = editor.Text.Length - 1;
            Assert.Equal(editor.Text.Length, editor.CaretOffset);
            editor.SetSelection(int.MaxValue, int.MaxValue);
            Assert.Equal(editor.Text.Length, editor.CaretOffset);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TypedTextAndDeletesFormSeparateUndoGroupsAndRestoreSelections()
    {
        var editor = new NeoEditor();
        var window = Show(editor);
        try
        {
            window.KeyTextInput("a");
            window.KeyTextInput("b");
            window.KeyTextInput("c");
            Assert.Equal("abc", editor.Text);
            Press(window, Key.Back, PhysicalKey.Backspace);
            Press(window, Key.Back, PhysicalKey.Backspace);
            Assert.Equal("a", editor.Text);
            editor.Undo();
            Assert.Equal("abc", editor.Text);
            editor.Undo();
            Assert.Equal("", editor.Text);
            editor.Redo();
            Assert.Equal("abc", editor.Text);

            editor.SetSelection(3, -2);
            editor.InsertAtCaret("\U0001F600");
            Assert.Equal("a\U0001F600", editor.Text);
            editor.Undo();
            Assert.Equal("abc", editor.Text);
            Assert.Equal((1, 2, 1), (editor.SelectionStart, editor.SelectionLength, editor.CaretOffset));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void WrappedAndUnwrappedLayoutShareScrollGeometryAndPreserveTheDocument()
    {
        var editor = new NeoEditor { Width = 270, Height = 170 };
        var window = Show(editor);
        try
        {
            var longLine = new string('m', 370);
            editor.LoadDocument($"{longLine}\r\n{longLine}");
            editor.CaretOffset = longLine.Length;
            double width = editor.Extent.Width;
            Assert.True(width > editor.Viewport.Width);
            Assert.True(editor.Offset.X > 0);

            editor.WordWrap = true;
            Assert.Equal(0, editor.Offset.X);
            Assert.Equal(editor.Viewport.Width, editor.Extent.Width);
            Assert.True(editor.Extent.Height > editor.Viewport.Height);
            Assert.Equal(longLine.Length, editor.CaretOffset);
            Assert.Equal(2, editor.LineCount);
            Assert.False(editor.IsModified);

            editor.InsertAtCaret("x");
            editor.WordWrap = false;
            editor.Undo();
            Assert.Equal($"{longLine}\r\n{longLine}", editor.Text);
            Assert.Equal(longLine.Length, editor.CaretOffset);
            Assert.False(editor.IsModified);
            Assert.True(editor.Extent.Width >= width);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void VerticalNavigation_UsesTheCaretGeometryBeforePaintAndAfterAFontChange()
    {
        var editor = new NeoEditor { Width = 270, Height = 170 };
        var window = Show(editor);
        try
        {
            editor.LoadDocument("abcdef\txyz\r\nabcdef\txyz\r\nabcdef\txyz");
            editor.SetSelection(7, 0);
            editor.FontSize = 20;
            (int Line, int Column) caret = default;
            editor.CaretMoved += (_, position) => caret = position;
            Press(window, Key.Down, PhysicalKey.ArrowDown);
            Assert.Equal((2, 8), caret);
            editor.MoveCaretToLine(2);
            Assert.Equal((3, 1), caret);
            editor.SelectAll();
            Assert.Equal((3, 11), caret);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void WrappedOffsets_UpdateWhenPhysicalLinesAreInsertedAndUndone()
    {
        var editor = new NeoEditor { Width = 270, Height = 170, WordWrap = true };
        var window = Show(editor);
        try
        {
            string text = new string('x', 420) + "\r\nlast";
            editor.LoadDocument(text);
            editor.MoveCaretToLine(1);
            var originalHeight = editor.Extent.Height;

            editor.SetSelection(0, 0);
            editor.InsertAtCaret("new\r\n");
            Assert.True(editor.Extent.Height > originalHeight);
            Press(window, Key.Down, PhysicalKey.ArrowDown);
            Assert.InRange(editor.CaretOffset, "new\r\n".Length + 1, text.Length - 1);
            editor.Undo();
            Assert.Equal(text, editor.Text);
            Assert.Equal(originalHeight, editor.Extent.Height, precision: 5);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void AutomationValueProvider_ExposesCurrentTextAndEditsThroughHistory()
    {
        var editor = new NeoEditor();
        editor.LoadDocument("original");
        var peer = Assert.IsAssignableFrom<AutomationPeer>(ControlAutomationPeer.CreatePeerForElement(editor));
        var provider = Assert.IsAssignableFrom<IValueProvider>(peer.GetProvider<IValueProvider>());
        Assert.Equal(AutomationControlType.Edit, peer.GetAutomationControlType());
        Assert.Equal("original", provider.Value);
        provider.SetValue("updated");
        Assert.Equal("updated", editor.Text);
        Assert.True(editor.IsModified);
        editor.Undo();
        Assert.Equal("original", provider.Value);
    }

    [AvaloniaFact]
    public void InputMethod_CompositionDoesNotMutateTextUntilCommit()
    {
        var editor = new NeoEditor();
        var window = Show(editor);
        try
        {
            editor.LoadDocument("alpha omega");
            editor.SetSelection(0, 5);
            var request = new TextInputMethodClientRequestedEventArgs { RoutedEvent = InputElement.TextInputMethodClientRequestedEvent };
            editor.RaiseEvent(request);
            var client = Assert.IsAssignableFrom<TextInputMethodClient>(request.Client);
            Assert.True(client.SupportsPreedit);
            Assert.Equal("alpha omega", client.SurroundingText);
            Assert.Equal(new TextSelection(0, 5), client.Selection);
            client.SetPreeditText("\u65E5", 1);
            Assert.False(editor.IsModified);
            Assert.Equal("alpha omega", editor.Text);
            Assert.True(client.CursorRectangle.Height > 0);

            window.KeyTextInput("\u65E5\u672C");
            Assert.Equal("\u65E5\u672C omega", editor.Text);
            editor.Undo();
            Assert.Equal("alpha omega", editor.Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Paste_AppliesTheChosenPolicyWithoutNormalizingExistingText()
    {
        var editor = new NeoEditor { NewLine = "\n" };
        var window = Show(editor);
        try
        {
            await window.Clipboard!.SetTextAsync("A\r\nB\rC\n");
            editor.LoadDocument("old\r");
            editor.CaretOffset = editor.Text.Length;
            await editor.PasteAsync();
            Assert.Equal("old\rA\nB\nC\n", editor.Text);
            editor.Undo();
            editor.PreservePastedLineEndings = true;
            await editor.PasteAsync();
            Assert.Equal("old\rA\r\nB\rC\n", editor.Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("e\u0301")]
    [InlineData("\U0001F1EE\U0001F1F1")]
    [InlineData("\U0001F468\u200D\U0001F469\u200D\U0001F467")]
    public void ArrowKeysAndDeletion_TreatExtendedGraphemesAsOneCharacter(string cluster)
    {
        var editor = new NeoEditor();
        var window = Show(editor);
        try
        {
            editor.LoadDocument("x" + cluster + "y");
            editor.CaretOffset = 1;
            Press(window, Key.Right, PhysicalKey.ArrowRight);
            Assert.Equal(1 + cluster.Length, editor.CaretOffset);
            Press(window, Key.Left, PhysicalKey.ArrowLeft);
            Assert.Equal(1, editor.CaretOffset);
            Press(window, Key.Delete, PhysicalKey.Delete);
            Assert.Equal("xy", editor.Text);
            editor.Undo();
            editor.CaretOffset = 1 + cluster.Length;
            Press(window, Key.Back, PhysicalKey.Backspace);
            Assert.Equal("xy", editor.Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void RightToLeftCharacters_MoveTheCaretInTheVisualDirection()
    {
        var editor = new NeoEditor();
        var window = Show(editor);
        try
        {
            editor.LoadDocument("\u05D0\u05D1\u05D2");
            editor.CaretOffset = 1;
            Press(window, Key.Left, PhysicalKey.ArrowLeft);
            Assert.Equal(2, editor.CaretOffset);
            Press(window, Key.Right, PhysicalKey.ArrowRight);
            Assert.Equal(1, editor.CaretOffset);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void MixedDirectionCharacters_MoveVisuallyInsideTheRightToLeftRun()
    {
        var editor = new NeoEditor();
        var window = Show(editor);
        try
        {
            editor.LoadDocument("abc \u05D0\u05D1\u05D2 xyz");
            editor.CaretOffset = 5;
            Press(window, Key.Left, PhysicalKey.ArrowLeft);
            Assert.Equal(6, editor.CaretOffset);
            Press(window, Key.Right, PhysicalKey.ArrowRight);
            Assert.Equal(5, editor.CaretOffset);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TypingWithAltGr_DoesNotRunTheUndoShortcut()
    {
        var editor = new NeoEditor();
        var window = Show(editor);
        try
        {
            editor.InsertAtCaret("x");
            Press(window, Key.Z, PhysicalKey.Z, RawInputModifiers.Control | RawInputModifiers.Alt);
            window.KeyTextInput("\u017C");
            Assert.Equal("x\u017C", editor.Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void InputMethod_CancelThenCommitOverMultilineSelection_PreservesUndoOrientation()
    {
        var editor = new NeoEditor();
        var window = Show(editor);
        try
        {
            editor.LoadDocument("a\r\nb\nc");
            editor.SetSelection(5, -4);
            var request = new TextInputMethodClientRequestedEventArgs { RoutedEvent = InputElement.TextInputMethodClientRequestedEvent };
            editor.RaiseEvent(request);
            var client = Assert.IsAssignableFrom<TextInputMethodClient>(request.Client);
            client.SetPreeditText("\u65E5", 1);
            client.SetPreeditText(null);
            Assert.Equal("a\r\nb\nc", editor.Text);
            Assert.Equal((5, 1, 4), (editor.SelectionAnchor, editor.CaretOffset, editor.SelectionLength));
            Assert.False(editor.IsModified);
            client.SetPreeditText("\u672C", 1);
            window.KeyTextInput("\u672C");
            Assert.Equal("a\u672Cc", editor.Text);
            editor.Undo();
            Assert.Equal("a\r\nb\nc", editor.Text);
            Assert.Equal((5, 1, 4), (editor.SelectionAnchor, editor.CaretOffset, editor.SelectionLength));
            Assert.False(editor.IsModified);
        }
        finally { window.Close(); }
    }

    private static Window Show(NeoEditor editor)
    {
        var window = new Window { Width = 300, Height = 200, Content = editor };
        window.Show();
        window.UpdateLayout();
        Assert.True(editor.Focus());
        return window;
    }

    private static void Press(Window window, Key key, PhysicalKey physicalKey, RawInputModifiers modifiers = default) =>
        window.KeyPress(key, modifiers, physicalKey, null);
}
