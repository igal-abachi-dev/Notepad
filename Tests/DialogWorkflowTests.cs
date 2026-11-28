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
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Notepad.Tests;

public class DialogWorkflowTests
{
    [AvaloniaFact]
    public async Task CommentCommands_UseTheActiveLanguageAndUndoAsOneEdit()
    {
        using var fixture = new MainWindowFixture();
        var vm = fixture.ViewModel;
        var editor = vm.EditorControl!;
        const string code = "  first();\r\n\tsecond();\nthird();";
        editor.LoadDocument(code);
        Assert.False(vm.ToggleLineCommentCommand.CanExecute(null));
        vm.SetLanguageCommand.Execute(SyntaxLanguage.CSharp);
        int third = code.IndexOf("third", StringComparison.Ordinal);
        editor.SetSelection(third, -third);
        fixture.Press(Key.OemQuestion, PhysicalKey.Slash, RawInputModifiers.Control);
        Assert.Equal("  // first();\r\n\t// second();\nthird();", editor.Text);
        fixture.Press(Key.OemQuestion, PhysicalKey.Slash, RawInputModifiers.Control);
        Assert.Equal(code, editor.Text);
        editor.Undo();
        editor.Undo();
        Assert.Equal((third, 0), (editor.SelectionAnchor, editor.CaretOffset));

        editor.LoadDocument("a();\r\nb();");
        editor.SelectAll();
        fixture.Press(Key.OemQuestion, PhysicalKey.Slash, RawInputModifiers.Control | RawInputModifiers.Shift);
        Assert.Equal("/*a();\r\nb();*/", editor.Text);
        vm.ToggleBlockCommentCommand.Execute(null);
        Assert.Equal("a();\r\nb();", editor.Text);

        editor.LoadDocument("<example />");
        editor.SelectAll();
        vm.SetLanguageCommand.Execute(SyntaxLanguage.Xml);
        vm.ToggleLineCommentCommand.Execute(null);
        Assert.Equal("<!--<example />-->", editor.Text);
        vm.ToggleLineCommentCommand.Execute(null);
        Assert.Equal("<example />", editor.Text);
        vm.SetLanguageCommand.Execute(SyntaxLanguage.Json);
        Assert.False(vm.ToggleBlockCommentCommand.CanExecute(null));

        string jsonc = Path.Combine(fixture.Folder, "app.jsonc");
        await File.WriteAllTextAsync(jsonc, "{\"value\": 1}");
        Assert.True(await vm.LoadFileAsync(jsonc));
        Assert.True(vm.ToggleLineCommentCommand.CanExecute(null));
        vm.ToggleLineCommentCommand.Execute(null);
        Assert.Equal("// {\"value\": 1}", editor.Text);
    }

    [AvaloniaFact]
    public async Task FontDialog_ExplainsLigaturesAndSavesThePreviewSetting()
    {
        using var fixture = new MainWindowFixture();
        var vm = fixture.ViewModel;
        var change = vm.ChangeFontCommand.ExecuteAsync(null);
        var dialog = Assert.IsType<FontDialog>(Assert.Single(fixture.Window.OwnedWindows));
        var toggle = dialog.FindControl<CheckBox>("LigaturesBox")!;
        var preview = dialog.FindControl<TextBlock>("PreviewText")!;
        Assert.True(toggle.IsChecked);
        Assert.Contains("!=", preview.Text);
        Assert.Same(EditorFontFeatures.ForLigatures(true), preview.FontFeatures);
        toggle.IsChecked = false;
        Assert.Same(EditorFontFeatures.ForLigatures(false), preview.FontFeatures);
        DefaultButton(dialog).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await change.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.False(vm.EditorControl!.EnableFontLigatures);
        Assert.False(new SettingsService(Path.Combine(fixture.Folder, "settings.json")).LoadEditorSettings().EnableFontLigatures);
    }

    [AvaloniaFact]
    public async Task AutomaticLanguage_SniffsUntitledTextAndHonorsOverridesAndFilename()
    {
        using var fixture = new MainWindowFixture();
        var vm = fixture.ViewModel;
        var editor = vm.EditorControl!;
        editor.InsertAtCaret("  {\"a\": 1}");
        Assert.Equal(SyntaxLanguage.Json, editor.Language);
        vm.SetLanguageCommand.Execute(SyntaxLanguage.Plain);
        editor.ReplaceRange(0, editor.TextLength, "\uFEFF\r\n<?xml version=\"1.0\"?><a />");
        Assert.Equal(SyntaxLanguage.Plain, editor.Language);
        Assert.Equal("Automatic (XML)", vm.AutomaticLanguageLabel);
        vm.SetLanguageCommand.Execute(null);
        Assert.Equal(SyntaxLanguage.Xml, editor.Language);
        editor.ReplaceRange(0, editor.TextLength, " <!doctype html><html></html>");
        Assert.Equal(SyntaxLanguage.Html, editor.Language);
        editor.Undo();
        Assert.Equal(SyntaxLanguage.Xml, editor.Language);

        string path = Path.Combine(fixture.Folder, "app.log");
        await File.WriteAllTextAsync(path, "{\"level\":\"INFO\"}");
        Assert.True(await vm.LoadFileAsync(path));
        Assert.Equal(SyntaxLanguage.Log, editor.Language);
    }

    [AvaloniaFact]
    public void HelpMenu_UsesTheProjectWebsiteAndShowsTheVersionInAbout()
    {
        using var fixture = new MainWindowFixture();
        var vm = fixture.ViewModel;
        var menu = fixture.Window.FindControl<Menu>("EditorMenu")!;
        var help = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "_Help"));
        var items = help.Items.OfType<MenuItem>().ToArray();
        Assert.Equal(new[] { "View _Help", "_About Notepad" }, items.Select(item => item.Header));
        Assert.Same(vm.ViewHelpCommand, items[0].Command);
        Assert.Equal("https://github.com/igal-abachi-dev/Notepad", MainWindowViewModel.ProjectWebsite);

        items[1].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        var dialog = Assert.IsType<AboutDialog>(Assert.Single(fixture.Window.OwnedWindows));
        Assert.StartsWith("Version ", dialog.FindControl<TextBlock>("VersionText")!.Text);
        DefaultButton(dialog).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Empty(fixture.Window.OwnedWindows);
    }

    [AvaloniaFact]
    public async Task TailFile_TracksAppendAndTruncateInAReadOnlyCleanDocument()
    {
        using var fixture = new MainWindowFixture();
        string path = Path.Combine(fixture.Folder, "live.log");
        await File.WriteAllTextAsync(path, "first\n");
        var vm = fixture.ViewModel;
        Assert.True(await vm.LoadFileAsync(path));
        var editor = vm.EditorControl!;
        var view = fixture.Window.FindControl<Menu>("EditorMenu")!.Items.OfType<MenuItem>()
            .Single(item => Equals(item.Header, "_View"));
        var tail = view.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "_Tail File"));
        Assert.DoesNotContain(fixture.Window.GetVisualDescendants().OfType<Button>(),
            button => ReferenceEquals(button.Command, vm.ToggleTailFileCommand));
        tail.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        await vm.ToggleTailFileCommand.ExecutionTask!.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(vm.IsFollowingLog);
        Assert.True(tail.IsChecked);
        Assert.True(editor.IsReadOnly);
        Assert.Equal(editor.TextLength, editor.CaretOffset);
        editor.InsertAtCaret("accidental");
        Assert.Equal("first\n", editor.Text);

        await File.AppendAllTextAsync(path, "second\n");
        await UntilAsync(() => editor.Text == "first\nsecond\n");
        Assert.Equal(editor.TextLength, editor.CaretOffset);
        Assert.False(vm.Document.IsModified);
        await File.WriteAllTextAsync(path, "rotated\n");
        await UntilAsync(() => editor.Text == "rotated\n");
        Assert.False(editor.IsModified);

        await vm.ToggleTailFileCommand.ExecuteAsync(null);
        Assert.False(editor.IsReadOnly);
        editor.InsertAtCaret("third\n");
        await vm.SaveFileCommand.ExecuteAsync(null);
        Assert.Equal("rotated\nthird\n", await File.ReadAllTextAsync(path));
    }

    [AvaloniaFact]
    public async Task ReadOnlyAndReload_KeepTheWindowLockedWhileReplacingItsDiskSnapshot()
    {
        using var fixture = new MainWindowFixture();
        string path = Path.Combine(fixture.Folder, "notes.txt");
        await File.WriteAllTextAsync(path, "before");
        var vm = fixture.ViewModel;
        Assert.True(await vm.LoadFileAsync(path));
        var editor = vm.EditorControl!;
        vm.ToggleReadOnlyModeCommand.Execute(null);
        editor.SelectAll();
        Assert.True(vm.CopyCommand.CanExecute(null));
        Assert.False(vm.CutCommand.CanExecute(null));
        editor.InsertAtCaret("changed");
        Assert.Equal("before", editor.Text);
        await File.WriteAllTextAsync(path, "after");
        fixture.Press(Key.R, PhysicalKey.R, RawInputModifiers.Control);
        await UntilAsync(() => editor.Text == "after");
        Assert.True(editor.IsReadOnly);
        Assert.False(vm.Document.IsModified);
        vm.ToggleReadOnlyModeCommand.Execute(null);
        Assert.False(editor.IsReadOnly);
    }

    [AvaloniaFact]
    public async Task DosArt_UsesOem437AndMonospaceWithoutChangingWindowDefaults()
    {
        using var fixture = new MainWindowFixture();
        var vm = fixture.ViewModel;
        vm.Settings.WordWrap = true;
        string path = Path.Combine(fixture.Folder, "art.nfo");
        byte[] original = [0xDA, 0xC4, 0xBF, 0x0A];
        await File.WriteAllBytesAsync(path, original);
        Assert.True(await vm.LoadFileAsync(path));
        var editor = vm.EditorControl!;
        Assert.Equal("\u250C\u2500\u2510\n", editor.Text);
        Assert.Equal(FileEncodingType.OEM437, vm.Document.EncodingType);
        Assert.Contains("Consolas", vm.EditorFontFamily);
        Assert.False(editor.WordWrap);
        Assert.True(vm.Settings.WordWrap);
        await vm.SaveFileCommand.ExecuteAsync(null);
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
        await vm.NewFileCommand.ExecuteAsync(null);
        Assert.True(editor.WordWrap);
    }

    [AvaloniaFact]
    public void BrowserActions_AreAvailableForASelectionAndCtrlEUsesGoogleSearch()
    {
        using var fixture = new MainWindowFixture();
        var vm = fixture.ViewModel;
        var editor = vm.EditorControl!;
        var context = editor.ContextMenu!;
        var items = context.Items.OfType<MenuItem>().ToArray();
        var google = items.Single(item => Equals(item.Header, "Search with _Google"));
        var lookup = items.Single(item => Equals(item.CommandParameter, "Define"));
        var translate = items.Single(item => Equals(item.CommandParameter, "Translate"));
        var ai = items.Single(item => Equals(item.Header, "Google _AI Mode"));
        var shortcut = fixture.Window.KeyBindings.Single(binding => binding.Gesture.Key == Key.E);
        Assert.Same(vm.OpenSelectionInBrowserCommand, shortcut.Command);
        Assert.Null(shortcut.CommandParameter);
        Assert.All(new[] { google, lookup, translate, ai }, item => Assert.False(item.IsVisible));
        Assert.False(shortcut.Command!.CanExecute(null));

        editor.LoadDocument("before\r\nselected text\r\nafter");
        editor.SetSelection(21, -13);
        Assert.Equal("selected text", editor.GetSelectedText());
        Assert.All(new[] { google, lookup, translate, ai }, item => Assert.True(item.IsVisible));
        Assert.True(shortcut.Command.CanExecute(null));
        Assert.Equal(new[] { "Explain", "Summarize", "Rewrite" }, ai.Items.OfType<MenuItem>().Select(item => (string)item.CommandParameter!));
        Assert.All(new[] { google, lookup, translate }.Concat(ai.Items.OfType<MenuItem>()),
            item => Assert.Same(vm.OpenSelectionInBrowserCommand, item.Command));

        editor.CaretOffset = 0;
        Assert.All(new[] { google, lookup, translate, ai }, item => Assert.False(item.IsVisible));
        Assert.False(shortcut.Command.CanExecute(null));
    }

    [AvaloniaFact]
    public void StatusBar_ShowsLogicalLineCountAndZeroBasedCaretOffset()
    {
        using var fixture = new MainWindowFixture();
        var vm = fixture.ViewModel;
        var editor = vm.EditorControl!;
        Assert.Equal("Ln 1, Col 1 lines:1 (Pos 0)", vm.StatusText);

        editor.LoadDocument("a\r\n\U0001F600\nz\r");
        Assert.Equal("Ln 1, Col 1 lines:4 (Pos 0)", vm.StatusText);
        editor.CaretOffset = 6;
        Assert.Equal("Ln 3, Col 1 lines:4 (Pos 6)", vm.StatusText);

        editor.ReplaceRange(editor.TextLength, 0, "last\n");
        Assert.Equal("Ln 5, Col 1 lines:5 (Pos 13)", vm.StatusText);
        editor.Undo();
        Assert.Equal("Ln 3, Col 1 lines:4 (Pos 6)", vm.StatusText);
    }

    [AvaloniaFact]
    public async Task OpenLargeFile_CancelKeepsTheActiveDocumentAndOpenLoadsAfterConsent()
    {
        using var fixture = new MainWindowFixture(new LargeFileReader());
        string path = Path.Combine(fixture.Folder, "large.txt");
        await File.WriteAllTextAsync(path, "loaded");
        var vm = fixture.ViewModel;
        vm.EditorControl!.ReplaceRange(0, 0, "unsaved");

        var canceled = vm.LoadFileAsync(path);
        var prompt = Assert.Single(fixture.Window.OwnedWindows);
        Assert.Equal("Open large file", prompt.Title);
        Assert.True(DefaultButton(prompt).IsCancel);
        DefaultButton(prompt).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.False(await canceled.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal("unsaved", vm.EditorControl.Text);
        Assert.True(vm.Document.IsModified);

        var accepted = vm.LoadFileAsync(path);
        prompt = Assert.Single(fixture.Window.OwnedWindows);
        prompt.GetVisualDescendants().OfType<Button>().Single(button => !button.IsCancel)
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(await accepted.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal("loaded", vm.EditorControl.Text);
        Assert.False(vm.Document.IsModified);
    }

    [AvaloniaFact]
    public async Task ReplaceNulsWithSpaces_RequiresAnExplicitCommandAndCanBeUndone()
    {
        using var fixture = new MainWindowFixture();
        string path = Path.Combine(fixture.Folder, "controls.txt");
        const string original = "A\0B\r\n\0C";
        await File.WriteAllTextAsync(path, original, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        var vm = fixture.ViewModel;
        Assert.True(await vm.LoadFileAsync(path));
        var editor = vm.EditorControl!;
        Assert.Equal(original, editor.Text);
        Assert.False(vm.Document.IsModified);
        editor.SetSelection(5, -4);

        vm.ReplaceNulsWithSpacesCommand.Execute(null);
        Assert.Equal("A B\r\n C", editor.Text);
        Assert.Equal((1, 4, 1), (editor.SelectionStart, editor.SelectionLength, editor.CaretOffset));
        Assert.True(vm.Document.IsModified);
        Assert.Equal(original, await File.ReadAllTextAsync(path));

        editor.Undo();
        Assert.Equal(original, editor.Text);
        Assert.False(vm.Document.IsModified);
    }

    [AvaloniaFact]
    public async Task SaveAnsi_OnlySwitchesToUtf8WithConsent()
    {
        using var fixture = new MainWindowFixture();
        const string edited = "\uFF21\U0001F984";
        var ansi = new FileService().GetEncoding(FileEncodingType.ANSI);
        try { ansi.GetByteCount(edited); return; }
        catch (EncoderFallbackException) { }

        string path = Path.Combine(fixture.Folder, "ansi.txt");
        await File.WriteAllTextAsync(path, "original");
        var vm = fixture.ViewModel;
        Assert.True(await vm.LoadFileAsync(path, FileEncodingType.ANSI));
        vm.EditorControl!.ReplaceRange(0, "original".Length, edited);

        var canceled = vm.SaveFileCommand.ExecuteAsync(null);
        await UntilAsync(() => fixture.Window.OwnedWindows.Any(window => window.Title == "Save as UTF-8?"));
        var prompt = Assert.Single(fixture.Window.OwnedWindows);
        Assert.True(DefaultButton(prompt).IsCancel);
        DefaultButton(prompt).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await canceled.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("original", await File.ReadAllTextAsync(path));
        Assert.True(vm.Document.IsModified);

        var accepted = vm.SaveFileCommand.ExecuteAsync(null);
        await UntilAsync(() => fixture.Window.OwnedWindows.Any(window => window.Title == "Save as UTF-8?"));
        prompt = Assert.Single(fixture.Window.OwnedWindows);
        prompt.GetVisualDescendants().OfType<Button>().Single(button => !button.IsCancel)
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await accepted.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(Encoding.UTF8.GetBytes(edited), await File.ReadAllBytesAsync(path));
        Assert.Equal(FileEncodingType.UTF8, vm.Document.EncodingType);
        Assert.False(vm.Document.IsModified);
    }

    [AvaloniaFact]
    public async Task TextTools_MenuTransformsTheSelectionOnceAndSupportsUndo()
    {
        using var fixture = new MainWindowFixture();
        var vm = fixture.ViewModel;
        var editor = vm.EditorControl!;
        editor.LoadDocument("before café after");
        editor.SetSelection(11, -4);
        var menu = fixture.Window.FindControl<Menu>("EditorMenu")!;
        var edit = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "_Edit"));
        var convert = edit.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Convert / _Encode"));
        var base64 = convert.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Base64"));
        var encode = base64.Items.OfType<MenuItem>().Single(item => Equals(item.CommandParameter, "Base64Encode"));
        encode.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        await vm.ApplyTextToolCommand.ExecutionTask!.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("before Y2Fmw6k= after", editor.Text);
        Assert.True(vm.Document.IsModified);
        editor.Undo();
        Assert.Equal("before café after", editor.Text);
        Assert.Equal((11, 7), (editor.SelectionAnchor, editor.CaretOffset));
        Assert.False(vm.Document.IsModified);

        var invalid = vm.ApplyTextToolCommand.ExecuteAsync("JsonMinify");
        await UntilAsync(() => fixture.Window.OwnedWindows.Count == 1);
        DefaultButton(Assert.Single(fixture.Window.OwnedWindows)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await invalid.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("before café after", editor.Text);
        Assert.False(vm.Document.IsModified);

        const string malformedUnicode = """{"text":"\uD800"}""";
        editor.LoadDocument(malformedUnicode);
        invalid = vm.ApplyTextToolCommand.ExecuteAsync("JsonMinify");
        await UntilAsync(() => fixture.Window.OwnedWindows.Count == 1);
        DefaultButton(Assert.Single(fixture.Window.OwnedWindows)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await invalid.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(malformedUnicode, editor.Text);
        Assert.False(vm.Document.IsModified);
    }

    [AvaloniaFact]
    public async Task ConvertFileEncoding_IsPendingUntilSaveAndWritesTheRequestedBom()
    {
        using var fixture = new MainWindowFixture();
        var vm = fixture.ViewModel;
        string file = Path.Combine(fixture.Folder, "notes.txt");
        await File.WriteAllBytesAsync(file, "sample"u8.ToArray());
        Assert.True(await vm.LoadFileAsync(file));
        var menu = fixture.Window.FindControl<Menu>("EditorMenu")!;
        var tools = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "_Tools"));
        var convert = tools.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "_Convert File Encoding"));
        var option = convert.Items.OfType<MenuItem>().Single(item => Equals(item.CommandParameter, FileEncodingType.UTF8BOM));
        option.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Empty(fixture.Window.OwnedWindows);
        Assert.Equal(FileEncodingType.UTF8, vm.Document.EncodingType);
        Assert.Equal(FileEncodingType.UTF8BOM, vm.Document.SaveEncodingType);
        Assert.True(vm.Document.IsModified);
        Assert.Equal("UTF-8 with BOM", vm.EncodingText);
        Assert.Equal("sample"u8.ToArray(), await File.ReadAllBytesAsync(file));

        var editor = vm.EditorControl!;
        editor.InsertAtCaret("x");
        editor.Undo();
        Assert.True(vm.Document.IsModified);
        await vm.SaveFileCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(new byte[] { 0xef, 0xbb, 0xbf }.Concat("sample"u8.ToArray()).ToArray(), await File.ReadAllBytesAsync(file));
        Assert.False(vm.Document.IsModified);
    }

    [AvaloniaFact]
    public async Task ViewFileAsEncoding_ReopensFromTheMenuWithoutAnEncodingDialog()
    {
        using var fixture = new MainWindowFixture();
        var vm = fixture.ViewModel;
        var menu = fixture.Window.FindControl<Menu>("EditorMenu")!;
        var tools = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "_Tools"));
        var view = tools.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "_View File as Encoding"));
        Assert.False(view.IsEnabled);

        string path = Path.Combine(fixture.Folder, "big-endian.txt");
        byte[] original = [0, 65, 0, 66];
        await File.WriteAllBytesAsync(path, original);
        Assert.True(await vm.LoadFileAsync(path, FileEncodingType.UTF16LE));
        Assert.True(view.IsEnabled);
        var option = view.Items.OfType<MenuItem>().Single(item => Equals(item.CommandParameter, FileEncodingType.UTF16BE));
        option.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        await vm.ViewFileAsEncodingCommand.ExecutionTask!.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("AB", vm.EditorControl!.Text);
        Assert.Equal(FileEncodingType.UTF16BE, vm.Document.EncodingType);
        Assert.False(vm.Document.IsModified);
        Assert.Empty(fixture.Window.OwnedWindows);
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
    }

    [AvaloniaFact]
    public void ShowAllCharacters_ChangesOnlyTheEditorDisplay()
    {
        using var fixture = new MainWindowFixture();
        var vm = fixture.ViewModel;
        var editor = vm.EditorControl!;
        const string text = "a \t\0\r\nb\nc\rd";
        editor.LoadDocument(text);
        editor.SetSelection(4, -3);
        Assert.False(editor.ShowAllCharacters);
        vm.ToggleShowAllCharactersCommand.Execute(null);
        fixture.Window.UpdateLayout();
        Assert.True(editor.ShowAllCharacters);
        Assert.Equal(text, editor.Text);
        Assert.Equal(4, editor.LineCount);
        Assert.Equal((4, 1), (editor.SelectionAnchor, editor.CaretOffset));
        Assert.False(vm.Document.IsModified);
    }

    [AvaloniaFact]
    public async Task OpenContainingFolder_RequiresANamedDocumentAndPreservesEditsWhenFolderIsGone()
    {
        using var fixture = new MainWindowFixture();
        var vm = fixture.ViewModel;
        var menu = fixture.Window.FindControl<Menu>("EditorMenu")!;
        var fileMenu = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "_File"));
        var openFolder = fileMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Open Containing _Folder"));
        Assert.Same(vm.OpenContainingFolderCommand, openFolder.Command);
        Assert.False(openFolder.IsEnabled);
        await vm.OpenContainingFolderCommand.ExecuteAsync(null);
        Assert.Empty(fixture.Window.OwnedWindows);

        string folder = Directory.CreateDirectory(Path.Combine(fixture.Folder, "a folder")).FullName;
        string path = Path.Combine(folder, "script.ps1");
        await File.WriteAllTextAsync(path, "original");
        Assert.True(await vm.LoadFileAsync(path));
        Assert.True(openFolder.IsEnabled);
        var document = vm.Document;
        var editor = vm.EditorControl!;
        editor.ReplaceRange(0, editor.TextLength, "unsaved");
        editor.SetSelection(7, -3);
        File.Delete(path);
        Directory.Delete(folder);

        openFolder.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        await UntilAsync(() => fixture.Window.OwnedWindows.Count == 1);
        var dialog = Assert.Single(fixture.Window.OwnedWindows);
        Assert.Contains(dialog.GetVisualDescendants().OfType<TextBlock>(), text =>
            text.Text == "Cannot open the containing folder. The folder is unavailable.");
        DefaultButton(dialog).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await vm.OpenContainingFolderCommand.ExecutionTask!.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Same(document, vm.Document);
        Assert.True(vm.Document.IsModified);
        Assert.Equal("unsaved", editor.Text);
        Assert.Equal((7, 4), (editor.SelectionAnchor, editor.CaretOffset));
    }

    [AvaloniaFact]
    public void InsertGuid_MenuAndShortcutInsertDistinctCanonicalGuidsAndCanBeUndone()
    {
        using var fixture = new MainWindowFixture();
        var editor = fixture.ViewModel.EditorControl!;
        editor.LoadDocument("[replace]");
        editor.SetSelection(1, 7);
        Assert.True(editor.Focus());
        var modifier = OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;
        fixture.Press(Key.G, PhysicalKey.G, modifier | RawInputModifiers.Shift);
        Assert.Equal(38, editor.TextLength);
        Assert.Equal('[', editor.Text[0]);
        Assert.Equal(']', editor.Text[^1]);
        string first = editor.Text[1..^1];
        Assert.True(Guid.TryParseExact(first, "D", out var firstGuid));
        Assert.Equal(firstGuid.ToString("D"), first);
        Assert.Equal(37, editor.CaretOffset);
        Assert.True(fixture.ViewModel.Document.IsModified);
        Assert.Empty(fixture.Window.OwnedWindows);
        editor.Undo();
        Assert.Equal("[replace]", editor.Text);
        Assert.Equal("replace", editor.GetSelectedText());
        Assert.False(fixture.ViewModel.Document.IsModified);

        var menu = fixture.Window.FindControl<Menu>("EditorMenu")!;
        var edit = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "_Edit"));
        var insert = edit.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "_Insert GUID"));
        insert.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.True(Guid.TryParseExact(editor.Text[1..^1], "D", out var secondGuid));
        Assert.NotEqual(firstGuid, secondGuid);
    }

    [AvaloniaFact]
    public void MacNativeMenu_ProjectsCommandsShortcutsAndCheckedState()
    {
        using var fixture = new MainWindowFixture();
        var editorMenu = fixture.Window.FindControl<Menu>("EditorMenu")!;
        var nativeMenu = fixture.Window.CreateMacMenu(editorMenu.Items);
        var file = nativeMenu.Items.OfType<NativeMenuItem>().Single(item => item.Header == "File").Menu!;
        var edit = nativeMenu.Items.OfType<NativeMenuItem>().Single(item => item.Header == "Edit").Menu!;
        var format = nativeMenu.Items.OfType<NativeMenuItem>().Single(item => item.Header == "Format").Menu!;
        var save = file.Items.OfType<NativeMenuItem>().Single(item => item.Header == "Save");
        var redo = edit.Items.OfType<NativeMenuItem>().Single(item => item.Header == "Redo");
        var wrap = format.Items.OfType<NativeMenuItem>().Single(item => item.Header == "Word Wrap");

        Assert.Same(fixture.ViewModel.SaveFileCommand, save.Command);
        Assert.Equal(new KeyGesture(Key.S, KeyModifiers.Meta), save.Gesture);
        Assert.Equal(new KeyGesture(Key.Z, KeyModifiers.Meta | KeyModifiers.Shift), redo.Gesture);
        Assert.Equal(NativeMenuItemToggleType.CheckBox, wrap.ToggleType);
        fixture.ViewModel.Settings.WordWrap = true;
        Assert.True(wrap.IsChecked);
    }

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
        Assert.False(model.FindPreviousCommand.CanExecute(null));
        query.Text = "x";
        Assert.True(model.FindNextCommand.CanExecute(null));
        var previous = dialog.FindControl<Button>("FindPreviousButton")!;
        Assert.Same(model.FindPreviousCommand, previous.Command);
        await model.FindPreviousCommand.ExecuteAsync(null);
        Assert.Equal(0, editor.SelectionStart);
        Assert.Equal(1, editor.SelectionLength);

        editor.CaretOffset = 2;
        dialog.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
        await (model.FindNextCommand.ExecutionTask ?? Task.CompletedTask).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(4, editor.SelectionStart);
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
    public async Task FindPreview_FollowsTheViewportAndClearsWhenTheDialogCloses()
    {
        using var fixture = new MainWindowFixture();
        var vm = fixture.ViewModel;
        var editor = Assert.IsType<NeoEditor>(vm.EditorControl);
        string text = "needle\n" + string.Concat(Enumerable.Repeat("ordinary line\n", 100)) + "needle";
        editor.LoadDocument(text);
        vm.ShowFindDialogCommand.Execute(null);
        var dialog = Assert.Single(fixture.Window.OwnedWindows.OfType<FindReplaceDialog>());
        var query = dialog.FindControl<TextBox>("FindTextBox")!;
        query.Text = "needle";
        var options = Assert.IsType<FindReplaceViewModel>(dialog.DataContext);
        Assert.False(options.HighlightMatches);
        await vm.FindFromDialogAsync(reverse: false);
        Assert.Equal(6, editor.SelectionLength);
        Assert.Null(editor.ActiveSearchMatch);
        Assert.Empty(editor.PassiveSearchMatches);
        editor.CaretOffset = 0;
        options.HighlightMatches = true;
        await UntilAsync(() => editor.PassiveSearchMatches.SequenceEqual(new[] { (0, 6) }));

        await vm.FindFromDialogAsync(reverse: false);
        Assert.Equal((0, 6), editor.ActiveSearchMatch);
        editor.CaretOffset = 1;
        Assert.Null(editor.ActiveSearchMatch);

        editor.ScrollToLine(editor.LineCount - 1);
        await UntilAsync(() => editor.PassiveSearchMatches.SequenceEqual(new[] { (text.Length - 6, 6) }));
        dialog.Close();
        Assert.Empty(editor.PassiveSearchMatches);
        await vm.FindNextCommand.ExecuteAsync(null);
        Assert.Equal((text.Length - 6, 6), editor.ActiveSearchMatch);
        editor.ReplaceRange(text.Length - 6, 6, "changed");
        Assert.Null(editor.ActiveSearchMatch);
    }

    [AvaloniaFact]
    public async Task FindAndReplace_NoMatchesUsesInlineFeedbackAndLeavesTheDialogOpen()
    {
        using var fixture = new MainWindowFixture();
        var vm = fixture.ViewModel;
        vm.EditorControl!.LoadDocument("one two");
        vm.ShowFindDialogCommand.Execute(null);
        var dialog = Assert.Single(fixture.Window.OwnedWindows.OfType<FindReplaceDialog>());
        var options = Assert.IsType<FindReplaceViewModel>(dialog.DataContext);
        options.FindText = "missing";
        await options.FindNextCommand.ExecuteAsync(null);
        Assert.Equal("No matches found.", dialog.FindControl<TextBlock>("SearchFeedback")!.Text);
        Assert.True(dialog.IsVisible);
        Assert.Empty(dialog.OwnedWindows);

        options.FindText = "two";
        Assert.Null(options.SearchFeedback);
        await options.FindNextCommand.ExecuteAsync(null);
        Assert.Equal("two", vm.EditorControl.GetSelectedText());
        Assert.Null(options.SearchFeedback);

        vm.ShowReplaceDialogCommand.Execute(null);
        options = Assert.IsType<FindReplaceViewModel>(dialog.DataContext);
        options.FindText = "missing";
        await options.ReplaceAllCommand.ExecuteAsync(null);
        Assert.Equal("No matches found.", options.SearchFeedback);
        Assert.Empty(dialog.OwnedWindows);
        dialog.Close();

        await vm.FindPreviousCommand.ExecuteAsync(null);
        Assert.Contains("No matches found.", vm.StatusText);
        Assert.Empty(fixture.Window.OwnedWindows);
    }

    [AvaloniaFact]
    public async Task LanguagePicker_OverridesAutomaticDetectionUntilTheDocumentChanges()
    {
        using var fixture = new MainWindowFixture();
        var vm = fixture.ViewModel;
        var editor = Assert.IsType<NeoEditor>(vm.EditorControl);
        string path = Path.Combine(fixture.Folder, "sample.JSON");
        await File.WriteAllTextAsync(path, "{\"test\":true}");
        Assert.True(await vm.LoadFileAsync(path));
        Assert.Equal(SyntaxLanguage.Json, editor.Language);

        var button = fixture.Window.FindControl<Button>("LanguageButton")!;
        Assert.Equal("JSON", button.Content);
        var menu = Assert.IsType<MenuFlyout>(button.Flyout);
        var plain = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Plain Text"));
        Assert.Equal(SyntaxLanguage.Plain, plain.CommandParameter);
        vm.SetLanguageCommand.Execute(SyntaxLanguage.Plain);
        Assert.Equal("Plain Text", button.Content);
        Assert.Equal(SyntaxLanguage.Plain, editor.Language);
        Assert.False(vm.Document.IsModified);

        Assert.Equal("Automatic (JSON)", vm.AutomaticLanguageLabel);
        vm.SetLanguageCommand.Execute(null);
        Assert.Equal(SyntaxLanguage.Json, editor.Language);
        vm.SetLanguageCommand.Execute(SyntaxLanguage.Plain);
        Assert.True(await vm.LoadFileAsync(path));
        Assert.Equal(SyntaxLanguage.Json, editor.Language);

        vm.SetLanguageCommand.Execute(SyntaxLanguage.Python);
        await vm.NewFileCommand.ExecuteAsync(null);
        Assert.Equal("Plain Text", button.Content);
        Assert.Equal(SyntaxLanguage.Plain, editor.Language);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("bad\0.cs")]
    [InlineData("untitled")]
    public void AutomaticLanguage_MissingOrInvalidPathUsesPlainText(string? path) =>
        Assert.Equal(SyntaxLanguage.Plain, SyntaxMatcher.DetectLanguage(path));

    [AvaloniaFact]
    public async Task EncodingDialog_UsesTheSelectedFormatAndDistinguishesAutoDetectFromCancel()
    {
        using var fixture = new MainWindowFixture();
        var save = new EncodingDialog(FileEncodingType.UTF8);
        var saveTask = save.ShowDialog<EncodingDialog.EncodingOption?>(fixture.Window);
        var box = save.FindControl<ComboBox>("EncodingCombo")!;
        Assert.Equal(6, box.ItemCount);
        Assert.Contains(box.Items.OfType<EncodingDialog.EncodingOption>(), item => item.Type == FileEncodingType.OEM437);
        box.SelectedIndex = 3;
        DefaultButton(save).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(FileEncodingType.UTF8BOM, (await saveTask.WaitAsync(TimeSpan.FromSeconds(2)))!.Type);

        var open = new EncodingDialog(null, forOpening: true);
        var openTask = open.ShowDialog<EncodingDialog.EncodingOption?>(fixture.Window);
        var auto = open.FindControl<ComboBox>("EncodingCombo")!;
        Assert.Equal(7, auto.ItemCount);
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
    public async Task FilePicker_TypingSelectsAnEntryAndHeadersSortBothWays()
    {
        using var fixture = new MainWindowFixture();
        string folder = Directory.CreateDirectory(Path.Combine(fixture.Folder, "listing")).FullName;
        Directory.CreateDirectory(Path.Combine(folder, "Cabinet"));
        string alpha = Path.Combine(folder, "alpha.txt");
        string beta = Path.Combine(folder, "beta.log");
        string zeta = Path.Combine(folder, "zeta.txt");
        await File.WriteAllTextAsync(alpha, "alpha");
        await File.WriteAllTextAsync(beta, "beta");
        await File.WriteAllTextAsync(zeta, "zeta");
        File.SetLastWriteTimeUtc(alpha, new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(beta, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(zeta, new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));

        var picker = new FilePickerDialog(null, alpha, null);
        var result = picker.ShowDialog<FilePickerDialog.Choice?>(fixture.Window);
        var list = picker.FindControl<ListBox>("Entries")!;
        await UntilAsync(() => list.ItemCount == 3);
        picker.FindControl<ComboBox>("FilterBox")!.SelectedIndex = 1;
        await UntilAsync(() => list.ItemCount == 4);
        Assert.True(list.Focus());
        picker.KeyTextInput("ze");
        Assert.Equal("zeta.txt", Assert.IsType<FilePickerEntry>(list.SelectedItem).Name);
        var filename = picker.FindControl<TextBox>("FileNameBox")!;
        Assert.Equal("zeta.txt", filename.Text);
        filename.Text = "custom.txt";

        await Sort("NameHeader", "Name ▼", ["Cabinet", "zeta.txt", "beta.log", "alpha.txt"]);
        Assert.Equal("zeta.txt", Assert.IsType<FilePickerEntry>(list.SelectedItem).Name);
        Assert.Equal("custom.txt", filename.Text);
        await Sort("TypeHeader", "Type ▲", ["Cabinet", "beta.log", "alpha.txt", "zeta.txt"]);
        await Sort("TypeHeader", "Type ▼", ["Cabinet", "alpha.txt", "zeta.txt", "beta.log"]);
        await Sort("ModifiedHeader", "Modified ▲", ["Cabinet", "beta.log", "zeta.txt", "alpha.txt"]);
        await Sort("ModifiedHeader", "Modified ▼", ["Cabinet", "alpha.txt", "zeta.txt", "beta.log"]);
        picker.Close();
        Assert.Null(await result);

        async Task Sort(string control, string label, string[] names)
        {
            var header = picker.FindControl<Button>(control)!;
            header.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await UntilAsync(() => Equals(header.Content, label));
            Assert.Equal(names, list.Items.OfType<FilePickerEntry>().Select(item => item.Name));
        }
    }

    [AvaloniaFact]
    public async Task FilePicker_FolderSuggestionsNavigateFromTheKeyboard()
    {
        using var fixture = new MainWindowFixture();
        string first = Directory.CreateDirectory(Path.Combine(fixture.Folder, "Alpha")).FullName;
        string second = Directory.CreateDirectory(Path.Combine(fixture.Folder, "Alpine")).FullName;
        await File.WriteAllTextAsync(Path.Combine(fixture.Folder, "Also.txt"), "excluded from suggestions");
        var picker = new FilePickerDialog(null, Path.Combine(fixture.Folder, "Also.txt"), null);
        picker.Styles.Add(new FluentTheme());
        var result = picker.ShowDialog<FilePickerDialog.Choice?>(fixture.Window);
        var address = picker.FindControl<TextBox>("FolderBox")!;
        var popup = picker.FindControl<Border>("FolderSuggestionPanel")!;
        var choices = picker.FindControl<ListBox>("FolderSuggestions")!;
        await UntilAsync(() => address.Text == fixture.Folder);
        Assert.True(address.Focus());
        address.Text = Path.Combine(fixture.Folder, "Al");
        await UntilAsync(() => popup.IsVisible);
        Assert.Equal(new[] { first, second }, choices.Items.OfType<string>());
        picker.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
        Assert.Equal(first, choices.SelectedItem);
        picker.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        await UntilAsync(() => address.Text == first && !popup.IsVisible);
        picker.Close();
        Assert.Null(await result);
    }

    [AvaloniaFact]
    public async Task FilePicker_F2RenamesFoldersAndFilesWithoutOverwriting()
    {
        using var fixture = new MainWindowFixture();
        string source = Path.Combine(fixture.Folder, "source.txt");
        string occupied = Path.Combine(fixture.Folder, "occupied.txt");
        await File.WriteAllTextAsync(source, "source");
        await File.WriteAllTextAsync(occupied, "existing");
        string originalFolder = Directory.CreateDirectory(Path.Combine(fixture.Folder, "original-folder")).FullName;
        var picker = new FilePickerDialog(null, source, FileEncodingType.UTF8);
        var result = picker.ShowDialog<FilePickerDialog.Choice?>(fixture.Window);
        var list = picker.FindControl<ListBox>("Entries")!;
        await UntilAsync(() => list.ItemCount == 3);

        await Rename(source, "occupied.txt", "Rename File");
        await UntilAsync(() => picker.FindControl<TextBlock>("Status")!.Text?.StartsWith("Cannot rename", StringComparison.Ordinal) == true);
        Assert.Equal("source", await File.ReadAllTextAsync(source));
        Assert.Equal("existing", await File.ReadAllTextAsync(occupied));

        await Rename(source, "renamed.txt", "Rename File");
        string renamed = Path.Combine(fixture.Folder, "renamed.txt");
        await UntilAsync(() => (list.SelectedItem as FilePickerEntry)?.Path == renamed);
        Assert.Equal("source", await File.ReadAllTextAsync(renamed));
        Assert.False(File.Exists(source));

        await Rename(originalFolder, "new-folder", "Rename Folder");
        string renamedFolder = Path.Combine(fixture.Folder, "new-folder");
        await UntilAsync(() => (list.SelectedItem as FilePickerEntry)?.Path == renamedFolder);
        Assert.True(Directory.Exists(renamedFolder));
        Assert.False(Directory.Exists(originalFolder));
        picker.Close();
        Assert.Null(await result);

        async Task Rename(string path, string name, string title)
        {
            list.SelectedItem = list.Items.OfType<FilePickerEntry>().Single(item => item.Path == path);
            Assert.True(list.Focus());
            picker.KeyPress(Key.F2, RawInputModifiers.None, PhysicalKey.F2, null);
            await UntilAsync(() => picker.OwnedWindows.Any());
            var prompt = Assert.Single(picker.OwnedWindows);
            Assert.Equal(title, prompt.Title);
            prompt.GetVisualDescendants().OfType<TextBox>().Single().Text = name;
            DefaultButton(prompt).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
    }

    [AvaloniaFact]
    public async Task RunScript_OnlyPromptsForUnsavedEditsAndCancelDoesNotSave()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new MainWindowFixture();
        string path = Path.Combine(fixture.Folder, "script.ps1");
        const string original = "Write-Output original";
        await File.WriteAllTextAsync(path, original);
        var vm = fixture.ViewModel;
        Assert.True(await vm.LoadFileAsync(path));
        Assert.True(vm.CanRunScript);
        var editor = vm.EditorControl!;
        editor.InsertAtCaret("# edited\r\n");

        var run = vm.RunScriptCommand.ExecuteAsync(null);
        var prompt = Assert.Single(fixture.Window.OwnedWindows);
        Assert.Equal("Save before running", prompt.Title);
        Assert.True(DefaultButton(prompt).IsCancel);
        DefaultButton(prompt).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await run.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(original, await File.ReadAllTextAsync(path));
        Assert.True(vm.Document.IsModified);

        editor.Undo();
        await File.WriteAllTextAsync(path, "changed externally");
        run = vm.RunScriptCommand.ExecuteAsync(null);
        await UntilAsync(() => fixture.Window.OwnedWindows.Any());
        prompt = Assert.Single(fixture.Window.OwnedWindows);
        Assert.Equal("Notepad", prompt.Title);
        Assert.Contains(prompt.GetVisualDescendants().OfType<TextBlock>(), item => item.Text?.Contains("Reopen it before running", StringComparison.Ordinal) == true);
        DefaultButton(prompt).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await run.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal("changed externally", await File.ReadAllTextAsync(path));
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
    public async Task FilePicker_HiddenToggleKeepsTheCurrentFilter()
    {
        using var fixture = new MainWindowFixture();
        string visible = Path.Combine(fixture.Folder, "visible.txt");
        string hidden = Path.Combine(fixture.Folder, ".hidden.txt");
        await File.WriteAllTextAsync(visible, "visible");
        await File.WriteAllTextAsync(hidden, "hidden");
        await File.WriteAllTextAsync(Path.Combine(fixture.Folder, ".hidden.md"), "filtered out");
        if (OperatingSystem.IsWindows())
            File.SetAttributes(hidden, FileAttributes.Hidden);
        var picker = new FilePickerDialog(visible, null, null);
        var result = picker.ShowDialog<FilePickerDialog.Choice?>(fixture.Window);
        var files = picker.FindControl<ListBox>("Entries")!;
        await UntilAsync(() => files.Items.Count == 1);
        Assert.Equal("visible.txt", Assert.Single(files.Items.OfType<FilePickerEntry>()).Name);

        picker.FindControl<CheckBox>("HiddenFilesBox")!.IsChecked = true;
        await UntilAsync(() => files.Items.Count == 2);
        Assert.Contains(files.Items.OfType<FilePickerEntry>(), entry => entry.Name == ".hidden.txt");
        picker.Close();
        await result;
    }

    [AvaloniaFact]
    public async Task FilePicker_UnixNameWithWildcardCharactersCanBeOpenedLiterally()
    {
        if (!(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())) return;
        using var fixture = new MainWindowFixture();
        const string name = " log_12:30\\*?.txt ";
        string path = Path.Combine(fixture.Folder, name);
        await File.WriteAllTextAsync(path, "data");
        var picker = new FilePickerDialog(null, path, null);
        var result = picker.ShowDialog<FilePickerDialog.Choice?>(fixture.Window);
        await UntilAsync(() => picker.FindControl<TextBox>("FolderBox")!.Text == fixture.Folder &&
            DefaultButton(picker).IsEnabled);
        picker.FindControl<TextBox>("FileNameBox")!.Text = name;
        DefaultButton(picker).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(path, (await result.WaitAsync(TimeSpan.FromSeconds(2)))?.Path);
    }

    [AvaloniaFact]
    public async Task Save_ReadOnlyFileRemainsEditableAndOffersSaveAs()
    {
        using var fixture = new MainWindowFixture();
        var vm = fixture.ViewModel;
        var editor = Assert.IsType<NeoEditor>(vm.EditorControl);
        string source = Path.Combine(fixture.Folder, "protected.txt");
        string destination = Path.Combine(fixture.Folder, "copy.txt");
        await File.WriteAllTextAsync(source, "original");
        var attributes = File.GetAttributes(source);
        File.SetAttributes(source, attributes | FileAttributes.ReadOnly);
        try
        {
            Assert.True(await vm.LoadFileAsync(source));
            Assert.Contains("[Read-only file]", fixture.Window.Title);
            Assert.False(editor.IsReadOnly);
            editor.InsertAtCaret("edited ");

            var save = vm.SaveFileCommand.ExecuteAsync(null);
            await UntilAsync(() => fixture.Window.OwnedWindows.Any(window => window.Title == "Read-only file"));
            var prompt = Assert.Single(fixture.Window.OwnedWindows);
            prompt.GetVisualDescendants().OfType<Button>().Single(button => button.IsCancel)
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await save.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(vm.Document.IsModified);
            Assert.Equal("edited original", editor.Text);

            save = vm.SaveFileCommand.ExecuteAsync(null);
            await UntilAsync(() => fixture.Window.OwnedWindows.Any(window => window.Title == "Read-only file"));
            DefaultButton(Assert.Single(fixture.Window.OwnedWindows)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await UntilAsync(() => fixture.Window.OwnedWindows.OfType<FilePickerDialog>().Any());
            var picker = Assert.Single(fixture.Window.OwnedWindows.OfType<FilePickerDialog>());
            await UntilAsync(() => picker.FindControl<TextBox>("FolderBox")!.Text == fixture.Folder &&
                DefaultButton(picker).IsEnabled);
            picker.FindControl<TextBox>("FileNameBox")!.Text = destination;
            DefaultButton(picker).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await save.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal("original", await File.ReadAllTextAsync(source));
            Assert.Equal("edited original", await File.ReadAllTextAsync(destination));
            Assert.Equal(destination, vm.Document.FilePath);
            Assert.False(vm.Document.IsModified);
            Assert.DoesNotContain("[Read-only file]", fixture.Window.Title);
        }
        finally { File.SetAttributes(source, attributes); }
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
        if (!(OperatingSystem.IsWindows() || OperatingSystem.IsLinux())) return;
        using var fixture = new MainWindowFixture();
        string path = Path.Combine(fixture.Folder, "linked.txt");
        string alias = Path.Combine(fixture.Folder, "alias.txt");
        await File.WriteAllTextAsync(path, "original");
        FileServiceTests.CreateHardLink(alias, path);
        Assert.True(await fixture.ViewModel.LoadFileAsync(path));
        fixture.ViewModel.EditorControl!.ReplaceRange(0, "original".Length, "updated");
        Assert.Empty(fixture.Window.OwnedWindows);

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

    private sealed class LargeFileReader : FileService
    {
        internal override async Task<LoadedFile?> ReadDocumentForEditorAsync(string path, FileEncodingType? requestedEncoding,
            bool windowsOnly, Func<long, Task<bool>> confirmLargeFile, bool replaceInvalidBytes = false)
        {
            if (!await confirmLargeFile(LargeFileWarningSize + 1L)) return null;
            return await base.ReadDocumentForEditorAsync(path, requestedEncoding, windowsOnly, confirmLargeFile, replaceInvalidBytes);
        }
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
