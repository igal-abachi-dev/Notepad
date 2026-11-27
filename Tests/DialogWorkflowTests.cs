using Avalonia.Controls;
using Avalonia.Automation;
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
    public async Task OpenInvalidUnicode_RefusalKeepsCurrentBuffer_AcceptanceCreatesAnUnsavedCopy()
    {
        using var fixture = new MainWindowFixture();
        string original = Path.Combine(fixture.Folder, "invalid.txt");
        byte[] bytes = [0xEF, 0xBB, 0xBF, 0x61, 0xC3];
        await File.WriteAllBytesAsync(original, bytes);
        var editor = Assert.IsType<NeoEditor>(fixture.ViewModel.EditorControl);
        editor.InsertAtCaret("old buffer");

        var refused = fixture.ViewModel.LoadFileAsync(original);
        var dialog = await WaitForInvalidEncodingPrompt(fixture.Window);
        dialog.GetVisualDescendants().OfType<Button>().Single(button => !button.IsDefault)
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.False(await refused.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal("old buffer", editor.Text);

        var accepted = fixture.ViewModel.LoadFileAsync(original);
        DefaultButton(await WaitForInvalidEncodingPrompt(fixture.Window)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(await accepted.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal("a\uFFFD", editor.Text);
        Assert.True(editor.IsModified);
        Assert.True(fixture.ViewModel.Document.IsUntitled);
        Assert.Null(fixture.ViewModel.Document.SavedFileVersion);
        Assert.Equal(FileEncodingType.UTF8, fixture.ViewModel.Document.SaveEncodingType);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(original));
    }

    [AvaloniaFact]
    public async Task FilePicker_FiltersEntriesAndKeepsSaveOpenWhenOverwriteIsDeclined()
    {
        using var fixture = new MainWindowFixture();
        string existing = Path.Combine(fixture.Folder, "existing.txt");
        Directory.CreateDirectory(Path.Combine(fixture.Folder, "folder"));
        await File.WriteAllTextAsync(existing, "original");
        await File.WriteAllTextAsync(Path.Combine(fixture.Folder, "other.md"), "markdown");
        var picker = new FilePickerDialog(existing, null, FileEncodingType.UTF8);
        var result = picker.ShowDialog<FilePickerDialog.Choice?>(fixture.Window);
        var files = picker.FindControl<ListBox>("Entries")!;
        await UntilAsync(() => files.Items.Count == 2);
        Assert.Equal(new[] { "folder", "existing.txt" }, files.Items.OfType<FilePickerEntry>().Select(entry => entry.Name));
        picker.FindControl<ComboBox>("FilterBox")!.SelectedIndex = 1;
        await UntilAsync(() => files.Items.Count == 3);

        var name = picker.FindControl<TextBox>("FileNameBox")!;
        Assert.Equal("existing.txt", name.Text);
        DefaultButton(picker).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await UntilAsync(() => picker.OwnedWindows.Count != 0);
        var overwrite = Assert.Single(picker.OwnedWindows);
        DefaultButton(overwrite).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(picker.IsVisible);
        Assert.False(result.IsCompleted);

        name.Text = "extensionless";
        DefaultButton(picker).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(new FilePickerDialog.Choice(Path.Combine(fixture.Folder, "extensionless"), FileEncodingType.UTF8, false),
            await result.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal("original", await File.ReadAllTextAsync(existing));
    }

    [AvaloniaFact]
    public async Task SaveAs_WhenAnotherWriterCreatesTheUnconfirmedPath_KeepsBothVersions()
    {
        using var fixture = new MainWindowFixture(new SaveCollisionFileService());
        var editor = Assert.IsType<NeoEditor>(fixture.ViewModel.EditorControl);
        editor.InsertAtCaret("our edits");
        string destination = Path.Combine(fixture.Folder, "new.txt");

        var save = fixture.ViewModel.SaveFileAsCommand.ExecuteAsync(null);
        var picker = Assert.Single(fixture.Window.OwnedWindows.OfType<FilePickerDialog>());
        var accept = picker.FindControl<Button>("AcceptButton")!;
        await UntilAsync(() => accept.IsEnabled);
        picker.FindControl<TextBox>("FileNameBox")!.Text = destination;
        accept.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await UntilAsync(() => fixture.Window.OwnedWindows.Any(window => window.Title == "Notepad"));
        var error = Assert.Single(fixture.Window.OwnedWindows.Where(window => window.Title == "Notepad"));
        DefaultButton(error).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await save.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("another writer", await File.ReadAllTextAsync(destination));
        Assert.Equal("our edits", editor.Text);
        Assert.True(fixture.ViewModel.Document.IsModified);
        Assert.True(fixture.ViewModel.Document.IsUntitled);
        Assert.Empty(Directory.GetFiles(fixture.Folder, ".notepad-*.tmp"));
    }

    private static async Task UntilAsync(Func<bool> ready)
    {
        for (int attempt = 0; attempt < 100; attempt++)
        {
            if (ready()) return;
            await Task.Delay(10);
        }
        Assert.True(ready(), "The dialog did not finish the requested operation.");
    }

    [AvaloniaFact]
    public async Task StartupMissingFile_AcceptingCreatesAnEmptyFileBeforeSave()
    {
        using var fixture = new MainWindowFixture();
        string path = Path.Combine(fixture.Folder, "new script.txt");
        var open = fixture.ViewModel.OpenStartupFileAsync(path.Replace('\\', '/'), null, print: false);
        var prompt = Assert.Single(fixture.Window.OwnedWindows);
        Assert.False(File.Exists(path));
        DefaultButton(prompt).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await open.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(0, new FileInfo(path).Length);
        Assert.False(fixture.ViewModel.Document.IsModified);
        Assert.Equal(path, fixture.ViewModel.Document.FilePath);
    }

    [AvaloniaFact]
    public async Task Find_WhenAlreadyOpenSeedsNewSelectionAndPastingTakesOnlyFirstLine()
    {
        using var fixture = new MainWindowFixture();
        var editor = Assert.IsType<NeoEditor>(fixture.ViewModel.EditorControl);
        editor.LoadDocument("first second");
        editor.SetSelection(0, 5);
        fixture.ViewModel.ShowFindDialogCommand.Execute(null);
        var find = Assert.Single(fixture.Window.OwnedWindows.OfType<FindReplaceDialog>());
        var query = find.FindControl<TextBox>("FindTextBox")!;
        Assert.Equal("first", query.Text);
        editor.SetSelection(6, 6);
        fixture.ViewModel.ShowFindDialogCommand.Execute(null);
        Assert.Same(find, Assert.Single(fixture.Window.OwnedWindows));
        Assert.Equal("second", query.Text);
        await find.Clipboard!.SetTextAsync("search\r\nignored");
        find.UpdateLayout();
        query.Focus();
        query.SelectAll();
        query.Paste();
        await UntilAsync(() => query.Text == "search");
        Assert.Equal("search", fixture.ViewModel.SearchSettings.SearchString);
    }

    [AvaloniaFact]
    public async Task StatusBar_ConvertsLineEndingsAndShowsOvertypeMode()
    {
        using var fixture = new MainWindowFixture();
        string path = Path.Combine(fixture.Folder, "mixed.txt");
        await File.WriteAllTextAsync(path, "one\r\ntwo\r\nthree\nfour");
        Assert.True(await fixture.ViewModel.LoadFileAsync(path));
        var button = fixture.Window.FindControl<Button>("LineEndingButton")!;
        Assert.Equal("CRLF", button.Content);
        fixture.ViewModel.ConvertLineEndingsCommand.Execute(LineEndingStyle.LF);
        Assert.Equal("LF", button.Content);
        var editor = Assert.IsType<NeoEditor>(fixture.ViewModel.EditorControl);
        Assert.Equal("one\ntwo\nthree\nfour", editor.Text);
        editor.Undo();
        Assert.Equal("CRLF", button.Content);
        Assert.False(editor.IsModified);

        Assert.True(editor.Focus());
        var indicator = fixture.Window.FindControl<TextBlock>("OvertypeIndicator")!;
        Assert.False(indicator.IsVisible);
        fixture.Press(Key.Insert, PhysicalKey.Insert);
        Assert.True(indicator.IsVisible);
    }

    [AvaloniaFact]
    public async Task GoToLine_ShortcutAndCommandRespectWordWrap()
    {
        using var fixture = new MainWindowFixture();
        var command = fixture.ViewModel.GoToLineCommand;
        fixture.ViewModel.Settings.WordWrap = true;
        Assert.False(command.CanExecute(null));
        fixture.Press(Key.G, PhysicalKey.G, RawInputModifiers.Control);
        await command.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Empty(fixture.Window.OwnedWindows);

        fixture.ViewModel.ToggleWordWrapCommand.Execute(null);
        Assert.True(command.CanExecute(null));
        fixture.Press(Key.G, PhysicalKey.G, RawInputModifiers.Control);
        var dialog = Assert.Single(fixture.Window.OwnedWindows.OfType<GoToLineDialog>());
        dialog.GetVisualDescendants().OfType<Button>().Single(button => button.IsCancel)
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await command.ExecutionTask!.WaitAsync(TimeSpan.FromSeconds(2));
    }

    private static async Task<Window> WaitForInvalidEncodingPrompt(Window owner)
    {
        for (int attempt = 0; attempt < 100; attempt++)
        {
            if (owner.OwnedWindows.FirstOrDefault(window => window.Title == "Invalid text encoding") is { } dialog)
                return dialog;
            await Task.Delay(10);
        }
        throw new TimeoutException("The invalid encoding prompt did not open.");
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

    [AvaloniaTheory]
    [InlineData("UpdateAllLinks")]
    [InlineData("SaveAs")]
    [InlineData("Cancel")]
    public async Task HardLinkedSave_OffersExplicitChoices(string choice)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new MainWindowFixture();
        string path = Path.Combine(fixture.Folder, "linked.txt");
        string alias = Path.Combine(fixture.Folder, "alias.txt");
        await File.WriteAllTextAsync(path, "original");
        FileServiceTests.CreateHardLink(alias, path);
        Assert.True(await fixture.ViewModel.LoadFileAsync(path));
        fixture.ViewModel.EditorControl!.ReplaceRange(0, "original".Length, "updated");

        var save = fixture.ViewModel.SaveFileCommand.ExecuteAsync(null);
        await UntilAsync(() => fixture.Window.OwnedWindows.Any(window => window.Title == "Save hard-linked file"));
        var prompt = fixture.Window.OwnedWindows.Single(window => window.Title == "Save hard-linked file");
        Assert.True(DefaultButton(prompt).IsCancel);
        var button = prompt.GetVisualDescendants().OfType<Button>()
            .Single(item => AutomationProperties.GetAutomationId(item) == $"MessageDialog{choice}");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        if (choice == "SaveAs")
        {
            await UntilAsync(() => fixture.Window.OwnedWindows.OfType<FilePickerDialog>().Any());
            var picker = fixture.Window.OwnedWindows.OfType<FilePickerDialog>().Single();
            await UntilAsync(() => picker.FindControl<TextBox>("FolderBox")!.Text == fixture.Folder &&
                picker.FindControl<Button>("AcceptButton")!.IsEnabled);
            picker.FindControl<TextBox>("FileNameBox")!.Text = "separate.txt";
            DefaultButton(picker).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        await save.WaitAsync(TimeSpan.FromSeconds(5));

        string expected = choice == "UpdateAllLinks" ? "updated" : "original";
        Assert.Equal(expected, await File.ReadAllTextAsync(path));
        Assert.Equal(expected, await File.ReadAllTextAsync(alias));
        Assert.Equal(choice == "Cancel", fixture.ViewModel.Document.IsModified);
        if (choice == "SaveAs")
            Assert.Equal("updated", await File.ReadAllTextAsync(Path.Combine(fixture.Folder, "separate.txt")));
        Assert.Empty(Directory.GetFiles(fixture.Folder, ".notepad-recovery-*.bak"));
    }

    private sealed class SaveCollisionFileService : FileService
    {
        internal override async Task<FileVersion> CommitNewFileAsync(string path, string content, FileEncodingType encodingType)
        {
            await File.WriteAllTextAsync(path, "another writer");
            return await base.CommitNewFileAsync(path, content, encodingType);
        }
    }

    private sealed class MainWindowFixture : IDisposable
    {
        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("notepad-dialog-tests-");
        public MainWindow Window { get; }
        public MainWindowViewModel ViewModel { get; }
        public string Folder => _directory.FullName;

        public MainWindowFixture(FileService? files = null)
        {
            ViewModel = new MainWindowViewModel(files ?? new FileService(), new SearchService(),
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
