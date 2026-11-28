using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Input.TextInput;
using Avalonia.Media;
using Notepad.NeoEdit;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Notepad.Tests;

public class NeoEditorTests
{
    [AvaloniaFact]
    public void Enter_UsesManualEditingByDefaultAndCopiesIndentWhenEnabled()
    {
        var editor = new NeoEditor { Text = "\t  statement" };
        var window = Show(editor);
        try
        {
            editor.CaretOffset = editor.TextLength;
            Press(window, Key.Enter, PhysicalKey.Enter);
            Assert.Equal("\t  statement\r\n", editor.Text);
            editor.Undo();

            editor.AutoIndent = true;
            Press(window, Key.Enter, PhysicalKey.Enter);
            Assert.Equal("\t  statement\r\n\t  ", editor.Text);
            editor.Undo();
            Assert.Equal("\t  statement", editor.Text);

            editor.CaretOffset = 1;
            Press(window, Key.Enter, PhysicalKey.Enter);
            Assert.Equal("\t\r\n\t  statement", editor.Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void LineShortcuts_InsertBelowMoveAndDuplicateWithoutSplittingTheActiveLine()
    {
        var editor = new NeoEditor { Text = "one\r\ntwo\nthree" };
        var window = Show(editor);
        try
        {
            editor.CaretOffset = 1;
            Press(window, Key.Enter, PhysicalKey.Enter, RawInputModifiers.Shift);
            Assert.Equal("one\r\n\r\ntwo\nthree", editor.Text);
            Assert.Equal(5, editor.CaretOffset);
            editor.Undo();
            editor.CaretOffset = 6;
            Press(window, Key.Up, PhysicalKey.ArrowUp, RawInputModifiers.Alt);
            Assert.Equal("two\r\none\nthree", editor.Text);
            Assert.Equal(1, editor.CaretOffset);
            Press(window, Key.Down, PhysicalKey.ArrowDown, RawInputModifiers.Alt | RawInputModifiers.Shift);
            Assert.Equal("two\r\ntwo\r\none\nthree", editor.Text);
            Assert.Equal(6, editor.CaretOffset);
            editor.Undo();
            editor.Undo();
            Assert.Equal("one\r\ntwo\nthree", editor.Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ColumnEditing_ReplacesAcrossRowsAndRestoresTheRectangleOnUndo()
    {
        var editor = new NeoEditor { Text = "abc\r\ndef\n", WordWrap = false };
        var window = Show(editor);
        try
        {
            editor.CaretOffset = 1;
            var modifiers = RawInputModifiers.Alt | RawInputModifiers.Shift;
            Press(window, Key.Right, PhysicalKey.ArrowRight, modifiers);
            Press(window, Key.Down, PhysicalKey.ArrowDown, modifiers);
            Press(window, Key.Down, PhysicalKey.ArrowDown, modifiers);
            Assert.True(editor.HasColumnSelection);
            Assert.Equal("b\r\ne\r\n", editor.GetSelectedText());
            await editor.CopyAsync();
            Assert.Equal("b\r\ne\r\n", await window.Clipboard!.TryGetTextAsync());

            window.KeyTextInput("Z");
            Assert.Equal("aZc\r\ndZf\n Z", editor.Text);
            Assert.True(editor.HasColumnSelection);
            editor.Undo();
            Assert.Equal("abc\r\ndef\n", editor.Text);
            Assert.Equal("b\r\ne\r\n", editor.GetSelectedText());

            await window.Clipboard!.SetTextAsync("1\n2\n3");
            await editor.PasteAsync();
            Assert.Equal("a1c\r\nd2f\n 3", editor.Text);
            Press(window, Key.Escape, PhysicalKey.Escape);
            Assert.False(editor.HasColumnSelection);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ReadOnly_AllowsKeyboardSelectionAndCopyWhileBlockingInteractiveEdits()
    {
        var editor = new NeoEditor { Text = "abc" };
        var window = Show(editor);
        try
        {
            editor.ReplaceRange(0, 1, "A");
            editor.CaretOffset = 0;
            editor.IsReadOnly = true;
            Assert.False(InputMethod.GetIsInputMethodEnabled(editor));
            Press(window, Key.Right, PhysicalKey.ArrowRight, RawInputModifiers.Shift);
            Assert.Equal("A", editor.GetSelectedText());

            var clipboard = window.Clipboard!;
            await clipboard.SetTextAsync("old");
            var primary = OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;
            Press(window, Key.C, PhysicalKey.C, primary);
            Assert.Equal("A", await clipboard.TryGetTextAsync());
            window.KeyTextInput("x");
            Press(window, Key.Delete, PhysicalKey.Delete);
            Press(window, Key.Back, PhysicalKey.Backspace);
            Press(window, Key.Enter, PhysicalKey.Enter);
            await editor.CutAsync();
            await editor.PasteAsync();
            editor.Undo();
            Assert.False(editor.CanUndo);
            Assert.Equal("Abc", editor.Text);

            var peer = ControlAutomationPeer.CreatePeerForElement(editor)!;
            var provider = Assert.IsAssignableFrom<IValueProvider>(peer.GetProvider<IValueProvider>());
            Assert.True(provider.IsReadOnly);
            Assert.Throws<InvalidOperationException>(() => provider.SetValue("blocked"));
            var nativeDocument = editor.CreateAutomationDocument();
            Assert.True(nativeDocument.IsReadOnly);
            Assert.True(nativeDocument.IsEnabled);
            Assert.Throws<InvalidOperationException>(() => nativeDocument.SetText("blocked"));

            editor.ReplaceRange(1, 1, "B");
            Assert.Equal("ABc", editor.Text);
            editor.IsReadOnly = false;
            Assert.True(InputMethod.GetIsInputMethodEnabled(editor));
            editor.Undo();
            Assert.Equal("Abc", editor.Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void DisplayAnnotations_AlignPanesWithoutChangingTextOrDocumentOffsets()
    {
        var left = new NeoEditor { Height = 42, Text = "first\r\nadded\r\nlast" };
        var right = new NeoEditor { Height = 42, Text = "first\r\nlast", IsReadOnly = true };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), Children = { left, right } };
        Grid.SetColumn(right, 1);
        var window = new Window { Content = grid, Width = 400, Height = 100 };
        window.Show();
        window.UpdateLayout();
        try
        {
            double rowHeight = left.ScrollSize.Height;
            long revision = right.DocumentVersion;
            left.SetLineSpacers([new(left.LineCount, 2)]);
            right.SetLineSpacers([new(1, 1), new(right.LineCount, 2)]);
            right.SetDecorations([new(0, Brushes.LightYellow, Brushes.Orange)], [new(0, 2, Brushes.Yellow)]);
            Assert.Equal(left.GetLineTop(2), right.GetLineTop(1), 6);
            Assert.Equal(left.Extent.Height, right.Extent.Height, 6);
            left.Offset = new Vector(0, rowHeight);
            right.Offset = new Vector(0, left.Offset.Y);
            Assert.Equal(left.Offset.Y, right.Offset.Y, 6);
            var gap = right.PointToScreen(new Point(40, rowHeight / 2));
            Assert.Equal(right.GetLineStartOffset(1), right.CreateAutomationDocument().HitTest(gap.X, gap.Y));

            right.SelectAll();
            Assert.Equal("first\r\nlast", right.GetSelectedText());
            Assert.Equal(2, right.LineCount);
            Assert.Equal(revision, right.DocumentVersion);
            Assert.False(right.IsModified);
            Assert.Throws<ArgumentOutOfRangeException>(() => right.SetLineSpacers([new(3, 1)]));
            Assert.Throws<ArgumentOutOfRangeException>(() => right.SetDecorations(inline: [new(0, 999, Brushes.Yellow)]));
            Assert.Equal(left.GetLineTop(2), right.GetLineTop(1), 6);

            right.WordWrap = true;
            Assert.Equal(rowHeight, right.GetLineTop(1), 6);
            right.WordWrap = false;
            Assert.Equal(left.GetLineTop(2), right.GetLineTop(1), 6);
            right.ReplaceRange(right.GetLineStartOffset(1), 4, "updated");
            Assert.Equal("first\r\nupdated", right.Text);
            Assert.Equal(rowHeight, right.GetLineTop(1), 6);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void SearchViewport_FollowsVisibleRowsInsideOneWrappedLine()
    {
        var editor = new NeoEditor { WordWrap = true, Width = 220, Height = 80 };
        var window = Show(editor);
        try
        {
            editor.LoadDocument(string.Concat(System.Linq.Enumerable.Repeat("one wrapped line ", 80)));
            window.UpdateLayout();
            var top = editor.GetVisibleTextRange();
            editor.Offset = new Vector(0, editor.Extent.Height / 2);
            var middle = editor.GetVisibleTextRange();

            Assert.Equal(0, top.Start);
            Assert.True(middle.Start > top.End);
            Assert.InRange(middle.End, middle.Start + 1, editor.TextLength - 1);
        }
        finally { window.Close(); }
    }

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

    [AvaloniaTheory]
    [InlineData("\u2764\uFE0F")]
    [InlineData("\U0001F469\u200D\u2764\uFE0F\u200D\U0001F469")]
    public void WordNavigationAndDeletion_KeepEmojiClustersTogether(string emoji)
    {
        var editor = new NeoEditor();
        var window = Show(editor);
        try
        {
            string original = "a" + emoji + "b";
            int afterEmoji = 1 + emoji.Length;
            editor.LoadDocument(original);
            editor.CaretOffset = 1;
            window.KeyPress(Key.Right, RawInputModifiers.Control, PhysicalKey.ArrowRight, null);
            Assert.Equal(afterEmoji, editor.CaretOffset);
            window.KeyPress(Key.Left, RawInputModifiers.Control, PhysicalKey.ArrowLeft, null);
            Assert.Equal(1, editor.CaretOffset);
            window.KeyPress(Key.Delete, RawInputModifiers.Control, PhysicalKey.Delete, null);
            Assert.Equal("ab", editor.Text);
            editor.Undo();
            Assert.Equal(original, editor.Text);
            Assert.Equal(1, editor.CaretOffset);
            editor.CaretOffset = afterEmoji;
            window.KeyPress(Key.Back, RawInputModifiers.Control, PhysicalKey.Backspace, null);
            Assert.Equal("ab", editor.Text);
            editor.Undo();
            Assert.Equal(original, editor.Text);
            Assert.Equal(afterEmoji, editor.CaretOffset);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void DoubleClick_SelectionKeepsEmojiClusterTogether()
    {
        const string emoji = "\u2764\uFE0F";
        var editor = new NeoEditor { ShowLineNumbers = false };
        var window = Show(editor);
        try
        {
            editor.LoadDocument(emoji + "next");
            var point = editor.TranslatePoint(new Point(5, 8), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Assert.Equal(emoji, editor.GetSelectedText());
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
    public void MarkdownLink_ModifierHoverUsesHandCursorAndClickUsesTheDestination()
    {
        const string markup = "[Notepad4](https://github.com/zufuliu/notepad4)[next](https://example.test/a_(b)?q=!)";
        var editor = new NeoEditor { Text = markup, ShowLineNumbers = false, WordWrap = false };
        var window = Show(editor);
        try
        {
            Assert.True(editor.Focus());
            Uri? requested = null;
            editor.WebLinkRequested += (_, link) => requested = link;
            var normal = editor.Cursor;
            int start = markup.IndexOf("https://", StringComparison.Ordinal);
            var first = Assert.IsType<NeoEditor.WebLinkRange>(editor.GetWebLinkRangeAt(start));
            Assert.Equal((start, "https://github.com/zufuliu/notepad4".Length), (first.Start, first.Length));
            Assert.Null(editor.GetWebLinkAt(first.Start + first.Length));
            int second = markup.IndexOf("https://example", StringComparison.Ordinal);
            Assert.Equal("https://example.test/a_(b)?q=!", editor.GetWebLinkAt(second)?.AbsoluteUri);

            var modifier = OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;
            Point position = default;
            for (int x = 70; x < 250 && ReferenceEquals(normal, editor.Cursor); x += 5)
            {
                position = editor.TranslatePoint(new Point(x, 8), window)!.Value;
                window.MouseMove(position, modifier);
            }
            Assert.NotSame(normal, editor.Cursor);
            window.MouseDown(position, MouseButton.Left, modifier);
            window.MouseUp(position, MouseButton.Left, modifier);
            Assert.Equal(first.Uri, requested);
            Assert.Equal(markup, editor.Text);
            window.MouseMove(position + new Vector(1, 0), RawInputModifiers.None);
            Assert.Same(normal, editor.Cursor);
        }
        finally { window.Close(); }
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
            editor.SetSelection(longLine.Length + 1, -9);
            editor.WordWrap = false;
            Assert.Equal((longLine.Length - 8, 9, longLine.Length - 8),
                (editor.SelectionStart, editor.SelectionLength, editor.CaretOffset));
            editor.WordWrap = true;
            Assert.Equal((longLine.Length - 8, 9, longLine.Length - 8),
                (editor.SelectionStart, editor.SelectionLength, editor.CaretOffset));
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
    public void TextInput_JoinsSplitSurrogatesWithoutCreatingAnInvalidEdit()
    {
        var editor = new NeoEditor();
        var window = Show(editor);
        try
        {
            editor.LoadDocument("first second");
            editor.SetSelection(5, -5);
            window.KeyTextInput("\uD83D");
            Assert.Equal("first", editor.GetSelectedText());
            Assert.False(editor.IsModified);
            Assert.False(editor.CanUndo);

            window.KeyTextInput("\uDE00");
            Assert.Equal("\U0001F600 second", editor.Text);
            Assert.Equal(2, editor.CaretOffset);
            editor.Undo();
            Assert.Equal("first second", editor.Text);
            Assert.Equal((5, 0), (editor.SelectionAnchor, editor.CaretOffset));
            Assert.False(editor.IsModified);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TextInput_DiscardsSurrogateFragmentsAfterOtherInputOrAnEditorChange()
    {
        var editor = new NeoEditor();
        var window = Show(editor);
        try
        {
            editor.LoadDocument("a");
            window.KeyTextInput("\uD83D");
            window.KeyTextInput("b");
            window.KeyTextInput("\uDE00");
            Assert.Equal("ba", editor.Text);

            window.KeyTextInput("\uD83D");
            editor.CaretOffset = 2;
            window.KeyTextInput("\uDE00");
            Assert.Equal("ba", editor.Text);

            window.KeyTextInput("\uD83D");
            editor.LoadDocument("new");
            window.KeyTextInput("\uDE00");
            Assert.Equal("new", editor.Text);
            Assert.False(editor.CanUndo);
        }
        finally { window.Close(); }
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
