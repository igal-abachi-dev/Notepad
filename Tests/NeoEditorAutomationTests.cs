using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Notepad.NeoEdit;
using Notepad.WindowsAutomation;
using System;
using System.Collections.Specialized;
using Xunit;

namespace Notepad.Tests;

public class NeoEditorAutomationTests
{
    [AvaloniaFact]
    public void FailedAutomationHostAttachment_KeepsEditingAndTheValueProviderAvailable()
    {
        if (!OperatingSystem.IsWindows()) return;

        var editor = new NeoEditor();
        var surface = new Grid { Children = { editor } };
        bool attachmentAttempted = false;
        surface.Children.CollectionChanged += (_, args) =>
        {
            if (args.Action != NotifyCollectionChangedAction.Add) return;
            attachmentAttempted = true;
            throw new InvalidOperationException("Simulated accessibility host failure.");
        };
        var window = new Window { Content = surface };
        window.Show();
        try
        {
            editor.AttachWindowsTextAutomation(window, surface);
            Assert.True(attachmentAttempted);
            Assert.Equal(0, editor.WindowsAutomationHandle);
            Assert.Null(editor.GetHostedTextPeer());
            Assert.True(editor.Focus());
            window.KeyTextInput("first");
            editor.SetSelection(0, 5);
            window.KeyTextInput("second");
            Assert.True(editor.IsModified);
            Assert.Equal("second", editor.GetText());
            editor.Undo();
            Assert.Equal("first", editor.GetSelectedText());
            editor.Redo();
            Assert.Equal("second", editor.GetText());
            editor.LoadDocument("reopened\r\ntext");
            Assert.False(editor.IsModified);
            var peer = ControlAutomationPeer.CreatePeerForElement(editor);
            var value = Assert.IsAssignableFrom<IValueProvider>(peer);
            Assert.Equal("reopened\r\ntext", value.Value);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TextUnits_RespectGraphemesWordsAndCrLf()
    {
        var editor = new NeoEditor();
        editor.LoadDocument("one two\r\ne\u0301 \U0001f600");
        var document = editor.CreateAutomationDocument();

        Assert.Equal("one ", document.GetText(document.UnitAt(1, TextUnit.Word)));
        Assert.Equal("one ", document.GetText(document.UnitAt(3, TextUnit.Word)));
        Assert.Equal("two", document.GetText(document.UnitAt(4, TextUnit.Word)));
        Assert.Equal("one two\r\n", document.GetText(document.UnitAt(4, TextUnit.Line)));
        Assert.Equal("\r\n", document.GetText(document.UnitAt(8, TextUnit.Character)));
        Assert.Equal("e\u0301", document.GetText(document.UnitAt(10, TextUnit.Character)));
        Assert.Equal("\U0001f600", document.GetText(document.UnitAt(document.Length - 1, TextUnit.Character)));
    }

    [AvaloniaFact]
    public void EditorSelection_ExposesNormalizedRangeAndActualCaret()
    {
        var editor = new NeoEditor();
        editor.LoadDocument("first\nsecond");
        var document = editor.CreateAutomationDocument();
        editor.SetSelection(12, -6);
        Assert.Equal(new TextSpan(6, 12), document.Selection);
        Assert.Equal(6, document.Caret);

        document.Select(new TextSpan(0, 5));
        Assert.Equal("first", editor.GetSelectedText());
        Assert.Equal(5, document.Caret);
    }

    [AvaloniaFact]
    public void VisibleRangesAndBoundingRectangles_UseTheWrappedEditorLayout()
    {
        var editor = new NeoEditor { WordWrap = true, ShowLineNumbers = false };
        var window = new Window { Width = 130, Height = 160, Content = editor };
        window.Show();
        try
        {
            editor.LoadDocument("alpha beta gamma delta epsilon zeta eta theta\r\nlast");
            window.UpdateLayout();
            var document = editor.CreateAutomationDocument();
            var first = document.UnitAt(0, TextUnit.Line);
            Assert.InRange(first.End, 1, 39);
            Assert.Equal(first.End, document.UnitAt(first.End, TextUnit.Line).Start);
            Assert.NotEmpty(document.GetVisibleRanges());
            var bounds = Assert.Single(document.GetBounds(new TextSpan(0, 1)));
            Assert.True(bounds.Width > 0 && bounds.Height > 0);
            Assert.InRange(document.HitTest(bounds.Left, bounds.Top + bounds.Height / 2), 0, 1);
        }
        finally { window.Close(); }
    }
}
