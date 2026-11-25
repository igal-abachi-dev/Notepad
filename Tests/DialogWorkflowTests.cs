using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Themes.Fluent;
using Avalonia.VisualTree;
using Notepad.NeoEdit;
using NotepadAvalonia.Models;
using NotepadAvalonia.Services;
using NotepadAvalonia.ViewModels;
using NotepadAvalonia.Views;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Notepad.Tests;

public class DialogWorkflowTests
{
    [AvaloniaFact]
    public async Task FindShortcuts_KeepMainAndModelessDialogDirectionsSeparate()
    {
        using var fixture = new MainWindowFixture();
        var editor = Assert.IsType<NeoEditor>(fixture.ViewModel.EditorControl);
        editor.LoadDocument("x---x");
        editor.CaretOffset = 2;
        fixture.Press(Key.F3, PhysicalKey.F3);
        var dialog = Assert.Single(fixture.Window.OwnedWindows.OfType<FindReplaceDialog>());
        var query = dialog.FindControl<TextBox>("FindTextBox")!;
        var model = Assert.IsType<FindReplaceViewModel>(dialog.DataContext);
        Assert.False(model.FindNextCommand.CanExecute(null));
        query.Text = "x";
        model.SearchUp = true;
        Assert.True(model.FindNextCommand.CanExecute(null));

        dialog.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
        await (model.FindNextCommand.ExecutionTask ?? Task.CompletedTask).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(0, editor.SelectionStart);
        Assert.Equal(1, editor.SelectionLength);

        editor.CaretOffset = 2;
        dialog.KeyPress(Key.F3, RawInputModifiers.None, PhysicalKey.F3, null);
        await fixture.ViewModel.FindNextCommand.ExecutionTask!.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(4, editor.SelectionStart);

        dialog.KeyPress(Key.F3, RawInputModifiers.Shift, PhysicalKey.F3, null);
        await fixture.ViewModel.FindPreviousCommand.ExecutionTask!.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(0, editor.SelectionStart);
        dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Assert.False(dialog.IsVisible);

        editor.CaretOffset = 2;
        editor.Focus();
        fixture.Press(Key.F3, PhysicalKey.F3);
        await fixture.ViewModel.FindNextCommand.ExecutionTask!.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(4, editor.SelectionStart);
    }

    [AvaloniaFact]
    public async Task EncodingDialog_UsesTheSelectedFormatAndDistinguishesAutoDetectFromCancel()
    {
        using var fixture = new MainWindowFixture();
        var save = new EncodingDialog(FileEncodingType.UTF8);
        var saveTask = save.ShowDialog<EncodingDialog.EncodingOption?>(fixture.Window);
        var box = save.FindControl<ComboBox>("EncodingCombo")!;
        Assert.Equal(5, box.ItemCount);
        box.SelectedIndex = 3;
        DefaultButton(save).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(FileEncodingType.UTF8BOM, (await saveTask.WaitAsync(TimeSpan.FromSeconds(2)))!.Type);

        var open = new EncodingDialog(null, forOpening: true);
        var openTask = open.ShowDialog<EncodingDialog.EncodingOption?>(fixture.Window);
        var auto = open.FindControl<ComboBox>("EncodingCombo")!;
        Assert.Equal(6, auto.ItemCount);
        DefaultButton(open).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Null(Assert.IsType<EncodingDialog.EncodingOption>(await openTask.WaitAsync(TimeSpan.FromSeconds(2))).Type);

        var cancelled = new EncodingDialog(null, forOpening: true);
        var cancelTask = cancelled.ShowDialog<EncodingDialog.EncodingOption?>(fixture.Window);
        cancelled.GetVisualDescendants().OfType<Button>().Single(button => button.IsCancel)
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Null(await cancelTask.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [AvaloniaFact]
    public async Task RecentMenu_OpensTheSpecificFileAndMainWindowBindingsUseTheEditorSettings()
    {
        using var fixture = new MainWindowFixture();
        string first = Path.Combine(fixture.Folder, "recent_one.txt");
        string second = Path.Combine(fixture.Folder, "recent_two.txt");
        await File.WriteAllTextAsync(first, "first");
        await File.WriteAllTextAsync(second, "second");
        await fixture.ViewModel.LoadFileAsync(first);
        await fixture.ViewModel.LoadFileAsync(second);

        var menu = fixture.Window.FindControl<MenuItem>("RecentMenu")!;
        var firstItem = menu.Items.OfType<MenuItem>().Single(item => Equals(first, item.CommandParameter));
        firstItem.Command!.Execute(firstItem.CommandParameter);
        await fixture.ViewModel.OpenRecentFileCommand.ExecutionTask!.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(first, fixture.ViewModel.Document.FilePath);
        var editor = Assert.IsType<NeoEditor>(fixture.ViewModel.EditorControl);
        Assert.Equal("first", editor.Text);

        var before = editor.FontSize;
        fixture.Press(Key.Add, PhysicalKey.NumPadAdd, RawInputModifiers.Control);
        Assert.True(editor.FontSize > before);
        fixture.Press(Key.D0, PhysicalKey.Digit0, RawInputModifiers.Control);
        Assert.Equal(before, editor.FontSize);
        fixture.ViewModel.ToggleWordWrapCommand.Execute(null);
        Assert.True(editor.WordWrap);
        Assert.True(fixture.ViewModel.Settings.ShowStatusBar);
        editor.InsertAtCaret("modified ");
        Assert.StartsWith("*", fixture.Window.Title);
        editor.Undo();
        Assert.Equal("recent_one.txt - Notepad", fixture.Window.Title);
    }

    [AvaloniaFact]
    public async Task Close_WhenDirtyUsesOnePromptAndCancelPreservesTheWindow()
    {
        using var fixture = new MainWindowFixture();
        var editor = Assert.IsType<NeoEditor>(fixture.ViewModel.EditorControl);
        editor.InsertAtCaret("unsaved");
        fixture.Window.Close();
        fixture.Window.Close();
        var prompt = Assert.Single(fixture.Window.OwnedWindows);
        prompt.GetVisualDescendants().OfType<Button>().Single(button => button.IsCancel)
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(fixture.Window.IsVisible);
        Assert.True(fixture.ViewModel.Document.IsModified);
        Assert.Equal("unsaved", editor.Text);

        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Window.Closed += (_, _) => closed.SetResult();
        fixture.Window.Close();
        var secondPrompt = Assert.Single(fixture.Window.OwnedWindows);
        secondPrompt.GetVisualDescendants().OfType<Button>().Single(button => !button.IsCancel && !button.IsDefault)
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [AvaloniaFact]
    public async Task PageSetup_InvalidMarginsKeepTheDialogOpen_ValidSettingsAreReturned()
    {
        using var fixture = new MainWindowFixture();
        var original = new PageSetupSettings();
        var dialog = new PageSetupDialog(original, new EditorSettings(), "Test", "one\r\ntwo");
        var result = dialog.ShowDialog<PageSetupSettings?>(fixture.Window);
        var orientation = dialog.FindControl<ComboBox>("OrientationBox")!;
        var left = dialog.FindControl<NumericUpDown>("LeftBox")!;
        left.Value = 400;
        DefaultButton(dialog).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.False(result.IsCompleted);
        Assert.True(dialog.FindControl<TextBlock>("Problem")!.IsVisible);

        orientation.SelectedIndex = 1;
        left.Value = 23;
        DefaultButton(dialog).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var chosen = Assert.IsType<PageSetupSettings>(await result.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.True(chosen.Landscape);
        Assert.Equal(23, chosen.MarginLeft);
        Assert.Equal(20, original.MarginLeft);
    }

    private static Button DefaultButton(Window window) =>
        window.GetVisualDescendants().OfType<Button>().Single(button => button.IsDefault);

    private sealed class MainWindowFixture : IDisposable
    {
        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("notepad-dialog-tests-");
        public MainWindow Window { get; }
        public MainWindowViewModel ViewModel { get; }
        public string Folder => _directory.FullName;

        public MainWindowFixture()
        {
            ViewModel = new MainWindowViewModel(new FileService(), new SearchService(),
                new SettingsService(Path.Combine(Folder, "settings.json")), new PrintService());
            Window = new MainWindow { DataContext = ViewModel };
            Window.Styles.Add(new FluentTheme());
            Window.Show();
            Window.UpdateLayout();
        }

        public void Press(Key key, PhysicalKey physicalKey, RawInputModifiers modifiers = default) =>
            Window.KeyPress(key, modifiers, physicalKey, null);

        public void Dispose()
        {
            Window.DataContext = null;
            if (Window.IsVisible) Window.Close();
            _directory.Delete(recursive: true);
        }
    }
}
