using Avalonia;
using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Themes.Fluent;
using Notepad.NeoEdit;
using NotepadAvalonia.Services;
using NotepadAvalonia.ViewModels;
using NotepadAvalonia.Views;
using Xunit;

namespace Notepad.Tests;

public class NeoEditorClipboardTests
{
    [AvaloniaFact]
    public async Task StandaloneEditor_ClipboardShortcutsWorkWithoutWindowCommands()
    {
        const string selected = "first\r\nsecond";
        var editor = new NeoEditor();
        var window = new Window { Content = editor };
        try
        {
            window.Show();
            Assert.True(editor.Focus());
            editor.LoadDocument(selected);
            editor.SetSelection(selected.Length, -selected.Length);
            var clipboard = Assert.IsAssignableFrom<IClipboard>(window.Clipboard);
            await clipboard.SetTextAsync("previous");
            var primary = OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;

            window.KeyPress(Key.C, primary, PhysicalKey.C, null);
            Assert.Equal(selected, await clipboard.TryGetTextAsync());
            Assert.Equal(selected, editor.GetSelectedText());
            window.KeyPress(Key.X, primary, PhysicalKey.X, null);
            Assert.Empty(editor.Text);
            window.KeyPress(Key.V, primary, PhysicalKey.V, null);
            Assert.Equal(selected, editor.Text);
            editor.Undo();
            Assert.Empty(editor.Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("middle", "new text")]
    [InlineData("first\r\nsecond", "one\r\ntwo\r\nthree")]
    public async Task Paste_WithActiveSelection_ReplacesItAndCollapsesToTheEnd(string selected, string replacement)
    {
        const string prefix = "left ";
        const string suffix = " right";
        string original = prefix + selected + suffix;
        var editor = new NeoEditor();
        var window = new Window { Content = editor };
        try
        {
            window.Show();
            editor.LoadDocument(original);
            var clipboard = Assert.IsAssignableFrom<IClipboard>(window.Clipboard);
            int start = prefix.Length;
            int end = start + selected.Length;
            editor.SetSelection(end, -selected.Length);
            await editor.CopyAsync();
            Assert.Equal((end, start), (editor.SelectionAnchor, editor.CaretOffset));
            Assert.Equal(selected, await clipboard.TryGetTextAsync());

            await editor.PasteAsync();
            Assert.Equal(original, editor.Text);
            Assert.Equal((end, end), (editor.SelectionAnchor, editor.CaretOffset));
            Assert.False(editor.IsModified);
            Assert.False(editor.CanUndo);

            editor.SetSelection(end, -selected.Length);
            await clipboard.SetTextAsync(replacement);
            await editor.PasteAsync();
            Assert.Equal(prefix + replacement + suffix, editor.Text);
            Assert.Equal(start + replacement.Length, editor.CaretOffset);
            Assert.Equal(0, editor.SelectionLength);
            editor.Undo();
            Assert.Equal(original, editor.Text);
            Assert.Equal((end, start), (editor.SelectionAnchor, editor.CaretOffset));
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MouseReleaseAndClipboardShortcuts_UseTheActualSelection(bool multiline)
    {
        var folder = Directory.CreateTempSubdirectory("notepad-clipboard-mouse-");
        var vm = new MainWindowViewModel(new FileService(), new SearchService(),
            new SettingsService(Path.Combine(folder.FullName, "settings.json")), new PrintService());
        var window = new MainWindow { DataContext = vm };
        window.Styles.Add(new FluentTheme());
        try
        {
            window.Show();
            window.UpdateLayout();
            var editor = Assert.IsType<NeoEditor>(vm.EditorControl);
            editor.ShowLineNumbers = false;
            editor.LoadDocument("abcd\r\nefgh");
            var start = editor.TranslatePoint(new Point(5, 8), window)!.Value;
            var end = editor.TranslatePoint(new Point(100, multiline ? 27 : 8), window)!.Value;
            window.MouseDown(start, MouseButton.Left);
            window.MouseUp(end, MouseButton.Left);
            string selected = multiline ? "abcd\r\nefgh" : "abcd";
            Assert.Equal(selected, editor.GetSelectedText());
            Assert.Same(vm, editor.ContextMenu!.DataContext);

            window.KeyPress(Key.C, RawInputModifiers.Control, PhysicalKey.C, "c");
            await vm.CopyCommand.ExecutionTask!;
            Assert.Equal(selected, await window.Clipboard!.TryGetTextAsync());
            editor.CaretOffset = editor.TextLength;
            window.KeyPress(Key.Insert, RawInputModifiers.Shift, PhysicalKey.Insert, null);
            await vm.PasteCommand.ExecutionTask!;
            Assert.Equal("abcd\r\nefgh" + selected, editor.Text);
            Assert.False(editor.OvertypeMode);
            editor.Undo();
            Assert.Equal("abcd\r\nefgh", editor.Text);
        }
        finally
        {
            window.DataContext = null;
            window.Close();
            folder.Delete();
        }
    }

    [AvaloniaFact]
    public async Task Cut_WithoutClipboard_PreservesTheSelection()
    {
        var editor = new NeoEditor();
        editor.LoadDocument("keep this");
        editor.SelectAll();

        await editor.CutAsync();

        Assert.Equal("keep this", editor.GetText());
        Assert.Equal(9, editor.SelectionLength);
    }

    [AvaloniaFact]
    public async Task MainWindow_ClipboardShortcuts_CutAndPasteThroughCommands()
    {
        var folder = Directory.CreateTempSubdirectory("notepad-clipboard-");
        var settings = new SettingsService(Path.Combine(folder.FullName, "settings.json"));
        var vm = new MainWindowViewModel(new FileService(), new SearchService(), settings, new PrintService());
        var window = new MainWindow { DataContext = vm };
        window.Styles.Add(new FluentTheme());
        try
        {
            window.Show();
            var editor = Assert.IsType<NeoEditor>(vm.EditorControl);
            var clipboard = Assert.IsAssignableFrom<IClipboard>(window.Clipboard);
            editor.LoadDocument("cut this");
            editor.SelectAll();
            Assert.True(editor.Focus());

            window.KeyPress(Key.X, RawInputModifiers.Control, PhysicalKey.X, "x");
            Assert.NotNull(vm.CutCommand.ExecutionTask);
            await vm.CutCommand.ExecutionTask;
            Assert.Equal("cut this", await clipboard.TryGetTextAsync());
            Assert.Equal("", editor.GetText());

            window.KeyPress(Key.V, RawInputModifiers.Control, PhysicalKey.V, "v");
            Assert.NotNull(vm.PasteCommand.ExecutionTask);
            await vm.PasteCommand.ExecutionTask;
            Assert.Equal("cut this", editor.GetText());
        }
        finally
        {
            window.DataContext = null;
            window.Close();
            folder.Delete();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReverseSelections_CopyCutUndoAndRedoWithoutLosingCharacters(bool multiline)
    {
        string selected = multiline ? "e\u0301\r\n\u05D0\u05D1\n\U0001F600" : "e\u0301 \U0001F600";
        string text = "left " + selected + " right";
        var editor = new NeoEditor();
        var window = new Window { Content = editor, Width = 420, Height = 240 };
        try
        {
            window.Show();
            Assert.True(editor.Focus());
            editor.LoadDocument(text);
            int start = "left ".Length;
            int end = start + selected.Length;
            editor.SetSelection(end, -selected.Length);
            Assert.Equal((end, start), (editor.SelectionAnchor, editor.CaretOffset));
            await editor.CopyAsync();
            Assert.Equal(selected, await window.Clipboard!.TryGetTextAsync());
            Assert.Equal(text, editor.Text);
            Assert.False(editor.IsModified);
            await editor.CutAsync();
            Assert.Equal("left  right", editor.Text);
            Assert.Equal(start, editor.CaretOffset);
            editor.Undo();
            Assert.Equal(text, editor.Text);
            Assert.Equal((end, start), (editor.SelectionAnchor, editor.CaretOffset));
            Assert.False(editor.IsModified);
            editor.Redo();
            Assert.Equal("left  right", editor.Text);
            Assert.Equal(0, editor.SelectionLength);
        }
        finally { window.Close(); }
    }
}
