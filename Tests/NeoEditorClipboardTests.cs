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
