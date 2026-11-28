using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Platform;
using Avalonia.VisualTree;
using Notepad.NeoEdit;
using NotepadAvalonia.Models;
using NotepadAvalonia.Services;
using NotepadAvalonia.Views;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace AvaloniaNotePad;

internal static partial class ReleaseSmokeTest
{
    public static async Task RunAsync(IClassicDesktopStyleApplicationLifetime desktop, MainWindow window, string reportPath)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
            Require(!System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported, "The running executable is Native AOT.");
            string directory = Path.GetDirectoryName(reportPath)!;
            var log = await ExerciseDialogsAsync(window, directory);
            await ExerciseEditorPolishAsync(window);
            log += Environment.NewLine + VerifyProgrammingLigatures();
            log += Environment.NewLine + "Language picker, opt-in Find highlights, Find Previous, and inline feedback passed.";
            log += Environment.NewLine + "Explicit NUL conversion and Undo passed.";
            log += Environment.NewLine + "Insert GUID menu and Undo passed.";
            log += Environment.NewLine + "Text tools and Show All Characters passed.";
            log += Environment.NewLine + "Google search shortcut and selection-only browser actions passed.";
            await ExerciseFilePickerAsync(window, directory);
            log += Environment.NewLine + "Avalonia Open, Save As, folder suggestions, sorting, and file encoding conversion passed.";
            await ExerciseReadOnlySaveAsync(window, directory);
            log += Environment.NewLine + "A read-only file remained editable and Save As preserved the original.";
            await ExerciseTailFileAsync(window, directory);
            log += Environment.NewLine + "Tail File followed an append without marking the document modified.";
            VerifyUnicodeSearch();
            log += Environment.NewLine + "Find Previous and Replace All passed with locale-specific matches and protected Unicode text.";
            if (OperatingSystem.IsWindows())
                log += Environment.NewLine + await ExerciseWindowsAsync(window, directory);
            File.WriteAllText(reportPath, log);
            if (window.IsVisible) desktop.Shutdown();
        }
        catch (Exception ex)
        {
            File.WriteAllText(reportPath, ex.ToString());
            desktop.Shutdown(1);
        }
    }

    private static string VerifyProgrammingLigatures()
    {
        var font = FontManager.Current.SystemFonts.FirstOrDefault(family => family.Name is "Cascadia Code" or "Fira Code" or "JetBrains Mono");
        if (font == null) return "Programming ligature glyph verification requires an installed coding font.";
        var probe = new NeoEditor { FontFamily = font, FontSize = 24, Language = SyntaxLanguage.CSharp };
        foreach (string symbol in new[] { "!=", "=>" })
        {
            string text = "public bool Example = left " + symbol + " right;";
            probe.EnableFontLigatures = true;
            using var enabled = probe.CreateLineTextLayout(text, FlowDirection.LeftToRight);
            probe.EnableFontLigatures = false;
            using var disabled = probe.CreateLineTextLayout(text, FlowDirection.LeftToRight);
            Require(!Glyphs(enabled).SequenceEqual(Glyphs(disabled)), $"{font.Name} shapes {symbol} differently with ligatures enabled.");
            int start = text.IndexOf(symbol, StringComparison.Ordinal);
            double left = enabled.HitTestTextPosition(start).X;
            double middle = enabled.HitTestTextPosition(start + 1).X;
            double right = enabled.HitTestTextPosition(start + 2).X;
            Require(left < middle && middle < right, "Caret positions stay distinct within the ligature.");
        }
        return $"{font.Name} programming ligature glyphs and caret positions passed.";

        static ushort[] Glyphs(TextLayout layout) => layout.TextLines.SelectMany(line => line.TextRuns)
            .OfType<ShapedTextRun>().SelectMany(run => run.ShapedBuffer).Select(glyph => glyph.GlyphIndex).ToArray();
    }

    private static async Task ExerciseEditorPolishAsync(MainWindow window)
    {
        var vm = window.ViewModel;
        var editor = vm.EditorControl!;
        editor.LoadDocument("A\0B");
        var aiMenu = editor.ContextMenu!.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Google _AI Mode"));
        Require(!aiMenu.IsVisible, "The browser prompt menu starts hidden without a selection.");
        editor.SelectAll();
        Require(aiMenu.IsVisible && aiMenu.Items.OfType<MenuItem>().All(item => ReferenceEquals(item.Command, vm.OpenSelectionInBrowserCommand)) &&
            window.KeyBindings.Any(binding => binding.Gesture.Key == Avalonia.Input.Key.E &&
                ReferenceEquals(binding.Command, vm.OpenSelectionInBrowserCommand) && binding.CommandParameter == null),
            "The selected-text prompts and Google Search shortcut are bound in the release build.");
        editor.CaretOffset = 0;
        var editMenu = window.FindControl<Menu>("EditorMenu")!.Items.OfType<MenuItem>()
            .Single(item => Equals(item.Header, "_Edit"));
        var replaceNuls = editMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Replace NUL Characters with _Spaces"));
        replaceNuls.Command!.Execute(replaceNuls.CommandParameter);
        Require(editor.Text == "A B", "The Edit menu replaces embedded NUL characters only on request.");
        editor.Undo();
        Require(editor.Text == "A\0B", "Undo restores embedded NUL characters.");
        editor.LoadDocument("");
        var insertGuid = editMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "_Insert GUID"));
        insertGuid.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Require(Guid.TryParseExact(editor.Text, "D", out _), "The Edit menu inserts a hyphenated GUID.");
        editor.Undo();
        Require(editor.TextLength == 0, "Undo removes the inserted GUID.");

        editor.LoadDocument("{ \"message\" : \"café\" }");
        await vm.ApplyTextToolCommand.ExecuteAsync("JsonMinify");
        Require(editor.Text == "{\"message\":\"café\"}", "JSON minification preserves Unicode data in the Native AOT build.");
        editor.Undo();
        Require(editor.Text == "{ \"message\" : \"café\" }", "One Undo restores the original JSON.");
        editor.LoadDocument("<root> <a/> </root>");
        await vm.ApplyTextToolCommand.ExecuteAsync("XmlMinify");
        Require(editor.Text == "<root><a /></root>", "XML minification works in the Native AOT build.");
        editor.Undo();
        Require(editor.Text == "<root> <a/> </root>" && !vm.Document.IsModified, "Undo restores the original XML fixture.");
        editor.LoadDocument("<!doctype html><b>a</b> <img  src = x >");
        await vm.ApplyTextToolCommand.ExecuteAsync("HtmlMinify");
        Require(editor.Text == "<!doctype html><b>a</b> <img src=x>", "HTML5 formatting keeps text and handles void tags.");
        editor.Undo();
        editor.LoadDocument("type Box<T> = { value: T; };\nconst n = <number>1;");
        await vm.ApplyTextToolCommand.ExecuteAsync("TsMinify");
        Require(editor.Text == "type Box<T> = {value: T;};\nconst n = <number>1;", "TypeScript formatting handles generics and assertions.");
        editor.Undo();
        vm.ToggleShowAllCharactersCommand.Execute(null);
        Require(editor.ShowAllCharacters, "The display toggle reaches the editor.");
        vm.ToggleShowAllCharactersCommand.Execute(null);

        Require(editor.SelectionBrush is ISolidColorBrush { Color.A: 15 }, "Light selections use the high-contrast text tint.");
        var language = window.FindControl<Button>("LanguageButton")!;
        var menu = (MenuFlyout)language.Flyout!;
        menu.ShowAt(language);
        var json = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "JSON"));
        json.Command!.Execute(json.CommandParameter);
        Require(Equals(language.Content, "JSON") && editor.Language == Notepad.NeoEdit.SyntaxLanguage.Json,
            "The language menu changed highlighting for the current document.");
        var automatic = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Automatic (Plain Text)"));
        automatic.Command!.Execute(automatic.CommandParameter);
        menu.Hide();
        Require(Equals(language.Content, "Plain Text"), "Automatic mode restored the document language.");

        editor.LoadDocument("marker marker");
        vm.ShowFindDialogCommand.Execute(null);
        var find = await WaitForDialog<FindReplaceDialog>(window);
        var highlights = find.FindControl<CheckBox>("HighlightMatchesBox")!;
        Require(highlights.IsChecked == false, "Find highlights are off by default.");
        find.FindControl<TextBox>("FindTextBox")!.Text = "marker";
        highlights.IsChecked = true;
        for (int attempt = 0; attempt < 100 && editor.PassiveSearchMatches.Count != 2; attempt++)
            await Task.Delay(20);
        Require(editor.PassiveSearchMatches.Count == 2, "Find preview marks visible matches when enabled.");
        await vm.FindNextCommand.ExecuteAsync(null);
        Require(editor.ActiveSearchMatch == (0, 6), "Find Next outlines the selected result.");
        await vm.FindNextCommand.ExecuteAsync(null);
        var previous = find.FindControl<Button>("FindPreviousButton")!;
        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)previous.Command!).ExecuteAsync(null);
        Require(editor.ActiveSearchMatch == (0, 6), "Find Previous selects the match above the current one.");
        find.FindControl<TextBox>("FindTextBox")!.Text = "missing";
        await vm.FindNextCommand.ExecuteAsync(null);
        Require(find.FindControl<TextBlock>("SearchFeedback")!.Text == "No matches found." && !find.OwnedWindows.Any(),
            "A failed search reports inline without opening a dialog.");
        find.Close();
        Require(editor.PassiveSearchMatches.Count == 0, "Closing Find clears the preview.");
    }

    private static void VerifyUnicodeSearch()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            var editor = new NeoEditor();
            var search = new SearchService();
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("hu-HU");
            editor.LoadDocument("dzs dzs");
            editor.CaretOffset = editor.TextLength;
            var settings = new SearchSettings { SearchString = "dzs", WholeWord = true };
            Require(search.FindPrevious(editor, settings).result == new SearchResult(4, 3),
                "Find Previous should select the full Hungarian contraction.");

            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            editor.LoadDocument("a\U000E0100b ab cafe\u0301");
            settings = new SearchSettings { SearchString = "ab", ReplaceString = "x", MatchCase = true };
            Require(search.ReplaceAll(editor, settings) == 1 && editor.Text == "a\U000E0100b x cafe\u0301",
                "Replace All should preserve an unrequested variation selector.");
            settings.SearchString = "caf\u00E9";
            Require(search.ReplaceAll(editor, settings) == 1 && editor.Text == "a\U000E0100b x x",
                "Replace All should replace the full decomposed match.");
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    private static async Task ExerciseTailFileAsync(MainWindow window, string directory)
    {
        string path = Path.Combine(directory, $"tail-{Guid.NewGuid():N}.log");
        await File.WriteAllTextAsync(path, "first\n");
        var vm = window.ViewModel;
        Require(await vm.LoadFileAsync(path), "The tail fixture opened.");
        await vm.ToggleTailFileCommand.ExecuteAsync(null);
        Require(vm.EditorControl is { IsReadOnly: true }, "Tail File makes the editor read-only.");
        await File.AppendAllTextAsync(path, "second\n");
        for (int attempt = 0; attempt < 125 && vm.EditorControl?.Text != "first\nsecond\n"; attempt++) await Task.Delay(20);
        Require(vm.EditorControl?.Text == "first\nsecond\n" && vm.EditorControl.CaretOffset == vm.EditorControl.TextLength &&
            !vm.Document.IsModified, "Tail File displays the append and follows the end.");
        await vm.ToggleTailFileCommand.ExecuteAsync(null);
        await vm.NewFileCommand.ExecuteAsync(null);
    }

    private static async Task ExerciseFilePickerAsync(MainWindow window, string directory)
    {
        var vm = window.ViewModel;
        var menu = window.FindControl<Menu>("EditorMenu")!;
        var fileMenu = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "_File"));
        var containingFolder = fileMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Open Containing _Folder"));
        Require(ReferenceEquals(containingFolder.Command, vm.OpenContainingFolderCommand) && !containingFolder.IsEnabled,
            "Open Containing Folder is bound and disabled for an untitled document.");
        string source = Path.Combine(directory, $"picker-input-{Guid.NewGuid():N}.txt");
        byte[] original = [0, 65, 0, 66];
        await File.WriteAllBytesAsync(source, original);
        vm.RecentFiles.Insert(0, source);

        var open = vm.OpenFileCommand.ExecuteAsync(null);
        var dialog = await WaitForDialog<FilePickerDialog>(window);
        await WaitForPickerFolderAsync(dialog, directory);
        string tag = "suggestion-" + Guid.NewGuid().ToString("N");
        string suggestedFolder = Directory.CreateDirectory(Path.Combine(directory, tag + "-folder")).FullName;
        var address = dialog.FindControl<TextBox>("FolderBox")!;
        var suggestions = dialog.FindControl<ListBox>("FolderSuggestions")!;
        var panel = dialog.FindControl<Border>("FolderSuggestionPanel")!;
        Require(address.Focus(), "The folder field can take keyboard focus.");
        address.Text = Path.Combine(directory, tag);
        for (int attempt = 0; attempt < 150 && !panel.IsVisible; attempt++) await Task.Delay(20);
        Require(panel.IsVisible && suggestions.Items.OfType<string>().Single() == suggestedFolder,
            "The folder field suggests matching directories in the Native AOT build.");
        dialog.FindControl<TextBox>("FileNameBox")!.Focus();
        Require(!panel.IsVisible, "Leaving the address field dismisses suggestions.");
        address.Text = directory;
        var sort = dialog.FindControl<Button>("NameHeader")!;
        sort.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        for (int attempt = 0; attempt < 100 && !Equals(sort.Content, "Name ▼"); attempt++) await Task.Delay(20);
        Require(Equals(sort.Content, "Name ▼"), "A file picker column can toggle its sort direction.");
        var encoding = dialog.FindControl<ComboBox>("EncodingBox")!;
        Require(encoding.ItemCount == 7 && (encoding.SelectedItem as EncodingDialog.EncodingOption)?.Name == "Auto-detect",
            "Open did not show Auto-detect and the six explicit encodings.");
        encoding.SelectedIndex = 3; // UTF-16 BE
        dialog.FindControl<TextBox>("FileNameBox")!.Text = Path.GetFileName(source);
        Accept(dialog);
        await open.WaitAsync(TimeSpan.FromSeconds(5));
        Require(vm.EditorControl?.Text == "AB" && vm.Document.EncodingType == FileEncodingType.UTF16BE,
            "Open did not use the file picker's selected encoding.");
        Require(containingFolder.IsEnabled, "Open Containing Folder is enabled after opening a file.");

        string name = $"picker-output-{Guid.NewGuid():N}";
        var save = vm.SaveFileAsCommand.ExecuteAsync(null);
        dialog = await WaitForDialog<FilePickerDialog>(window);
        await WaitForPickerFolderAsync(dialog, directory);
        encoding = dialog.FindControl<ComboBox>("EncodingBox")!;
        Require(encoding.ItemCount == 6 && (encoding.SelectedItem as EncodingDialog.EncodingOption)?.Type == FileEncodingType.UTF16BE,
            "Save As did not preselect the document encoding.");
        encoding.SelectedIndex = 3; // UTF-8 with BOM
        dialog.FindControl<TextBox>("FileNameBox")!.Text = name;
        Accept(dialog);
        await save.WaitAsync(TimeSpan.FromSeconds(5));
        string destination = Path.Combine(directory, name + ".txt");
        Require((await File.ReadAllBytesAsync(destination)).SequenceEqual(
            new byte[] { 0xEF, 0xBB, 0xBF, 65, 66 }), "Save As did not use the selected encoding and default extension.");
        Require((await File.ReadAllBytesAsync(source)).SequenceEqual(original), "Save As modified the source.");

        var tools = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "_Tools"));
        var convert = tools.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "_Convert File Encoding"));
        var utf8 = convert.Items.OfType<MenuItem>().Single(item => Equals(item.CommandParameter, FileEncodingType.UTF8));
        utf8.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Require(!window.OwnedWindows.Any(), "The Tools menu changes save encoding without an encoding dialog.");
        Require(vm.Document.IsModified && vm.EditorControl?.Text == "AB" && new FileInfo(destination).Length == 5,
            "File encoding conversion retains existing text and bytes until Save.");
        await vm.SaveFileCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(5));
        Require((await File.ReadAllBytesAsync(destination)).SequenceEqual(new byte[] { 65, 66 }) && !vm.Document.IsModified,
            "Saving the converted file removes the UTF-8 BOM.");
        var view = tools.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "_View File as Encoding"));
        var auto = view.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Auto-detect"));
        auto.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        await vm.ViewFileAsEncodingCommand.ExecutionTask!.WaitAsync(TimeSpan.FromSeconds(5));
        Require(vm.EditorControl?.Text == "AB" && !window.OwnedWindows.Any(), "Auto-detect reopens from the Tools menu without a picker.");
        await vm.NewFileCommand.ExecuteAsync(null);
        Require(vm.Document.IsUntitled && !containingFolder.IsEnabled, "New disables Open Containing Folder for the untitled document.");
    }

    private static async Task WaitForPickerFolderAsync(FilePickerDialog dialog, string directory)
    {
        for (int attempt = 0; attempt < 250; attempt++)
        {
            if (dialog.FindControl<TextBox>("FolderBox")?.Text == directory &&
                dialog.FindControl<Button>("AcceptButton")?.IsEnabled == true)
                return;
            await Task.Delay(20);
        }
        throw new TimeoutException("The Avalonia file picker did not load the test folder.");
    }

    private static async Task ExerciseReadOnlySaveAsync(MainWindow window, string directory)
    {
        var vm = window.ViewModel;
        string source = Path.Combine(directory, $"protected-{Guid.NewGuid():N}.txt");
        string destination = Path.Combine(directory, $"editable-copy-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(source, "original");
        var attributes = File.GetAttributes(source);
        File.SetAttributes(source, attributes | FileAttributes.ReadOnly);
        try
        {
            Require(await vm.LoadFileAsync(source) && window.Title?.Contains("[Read-only file]") == true,
                "The window identifies the protected source.");
            var editor = vm.EditorControl!;
            editor.InsertAtCaret("edited ");
            Require(editor.Text == "edited original", "The source opens in an editable buffer.");

            var save = vm.SaveFileCommand.ExecuteAsync(null);
            var prompt = await WaitForDialog<Window>(window);
            Require(prompt.Title == "Read-only file", "Save offers to write a separate file.");
            Accept(prompt);
            var picker = await WaitForDialog<FilePickerDialog>(window);
            await WaitForPickerFolderAsync(picker, directory);
            picker.FindControl<TextBox>("FileNameBox")!.Text = destination;
            Accept(picker);
            await save.WaitAsync(TimeSpan.FromSeconds(5));
            Require(await File.ReadAllTextAsync(source) == "original" &&
                await File.ReadAllTextAsync(destination) == "edited original", "The protected file stays intact.");
            Require(!vm.Document.IsReadOnlyFile && !vm.Document.IsModified && vm.Document.FilePath == destination,
                "The saved copy is the active writable document.");
            await vm.NewFileCommand.ExecuteAsync(null);
        }
        finally { File.SetAttributes(source, attributes); }
    }

    private static async Task<string> ExerciseDialogsAsync(MainWindow window, string directory)
    {
        var vm = window.ViewModel;
        Require(vm.EditorControl?.IsFocused == true && vm.EditorControl.TextLength == 0, "The blank editor has focus.");
        using var process = Process.GetCurrentProcess();
        double focusedMiB = process.WorkingSet64 / (1024.0 * 1024);
        double startupMilliseconds = (DateTime.UtcNow - process.StartTime.ToUniversalTime()).TotalMilliseconds;
        vm.ShowAboutCommand.Execute(null);
        var about = await WaitForDialog<AboutDialog>(window);
        Require(about.FindControl<TextBlock>("VersionText")?.Text?.StartsWith("Version ", StringComparison.Ordinal) == true,
            "About displays the running assembly version in the AOT build.");
        Accept(about);
        vm.SetThemeCommand.Execute(EditorTheme.Dark);
        Require(window.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark, "Dark theme applied to the window.");
        Require(vm.EditorControl?.Background is ISolidColorBrush { Color.A: 255 }, "Dark editor surface is opaque.");
        vm.SetThemeCommand.Execute(EditorTheme.Light);
        Require(window.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Light, "Light theme applied to the window.");
        Require(vm.EditorControl?.Background is ISolidColorBrush { Color.A: 255 }, "Light editor surface is opaque.");
        bool usesMica = window.ActualTransparencyLevel == WindowTransparencyLevel.Mica &&
            window.PlatformSettings?.GetColorValues().ContrastPreference != ColorContrastPreference.High;
        Require(window.Classes.Contains("mica-chrome") == usesMica, "Chrome follows the negotiated backdrop.");
        Require((window.Background is ISolidColorBrush { Color.A: 0 }) == usesMica, "Only the Mica chrome exposes the backdrop.");
        var selectEncoding = vm.ChangeDefaultEncodingCommand.ExecuteAsync(null);
        var encodingDialog = await WaitForDialog<EncodingDialog>(window);
        var options = encodingDialog.FindControl<ComboBox>("EncodingCombo")!;
        Require(options.ItemCount == 6 && options.SelectedItem is EncodingDialog.EncodingOption, "Encoding choices rendered.");
        options.SelectedIndex = 2;
        encodingDialog.UpdateLayout();
        Require(options.GetVisualDescendants().OfType<TextBlock>().Any(label => label.Text == "UTF-16 BE"),
            "The selected encoding label displays its name.");
        Accept(encodingDialog);
        await selectEncoding.WaitAsync(TimeSpan.FromSeconds(5));
        Require(vm.Document.SaveEncodingType == FileEncodingType.UTF16BE, "Encoding dialog applied UTF-16 BE.");

        var file = Path.Combine(directory, $"smoke-input-{Guid.NewGuid():N}.txt");
        vm.EditorControl!.LoadDocument("first\r\nsecond\r\nthird");
        var lineEndings = window.FindControl<Button>("LineEndingButton")!;
        var menu = (MenuFlyout)lineEndings.Flyout!;
        menu.ShowAt(lineEndings);
        var convertToLf = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Unix (LF)"));
        Require(convertToLf.Command != null, "The line-ending flyout did not bind to the editor.");
        convertToLf.Command!.Execute(convertToLf.CommandParameter);
        Require(vm.EditorControl.Text == "first\nsecond\nthird" && Equals(lineEndings.Content, "LF"),
            "The line-ending menu did not convert the document.");
        menu.Hide();
        vm.EditorControl.Undo();
        Require(Equals(lineEndings.Content, "CRLF") && !vm.EditorControl.IsModified,
            "Undo did not restore the line-ending mode and clean state.");
        var peer = ControlAutomationPeer.CreatePeerForElement(vm.EditorControl);
        Require(peer?.GetProvider<IValueProvider>()?.Value == "first\r\nsecond\r\nthird",
            "The published editor exposes the document through its automation peer.");
        if (!vm.Settings.WordWrap) vm.ToggleWordWrapCommand.Execute(null);
        Require(!vm.CanUseGoToLine && !vm.GoToLineCommand.CanExecute(null), "Word Wrap disables Go To Line.");
        await vm.GoToLineCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(5));
        Require(window.OwnedWindows.OfType<GoToLineDialog>().All(dialog => !dialog.IsVisible),
            "Go To Line stays closed with Word Wrap enabled.");
        vm.ToggleWordWrapCommand.Execute(null);
        Require(vm.GoToLineCommand.CanExecute(null), "Go To Line is available with Word Wrap disabled.");
        var goTo = vm.GoToLineCommand.ExecuteAsync(null);
        var goToDialog = await WaitForDialog<GoToLineDialog>(window);
        var range = goToDialog.FindControl<TextBlock>("InfoText");
        Require(range?.Text == "Line number (1 - 3)", "Go To Line shows the valid range.");
        var lineNumber = goToDialog.FindControl<TextBox>("LineNumberTextBox")!;
        lineNumber.Text = "4";
        Accept(goToDialog);
        Require(goToDialog.FindControl<TextBlock>("ValidationMessage")?.IsVisible == true, "Invalid line shows an error.");
        lineNumber.Text = "3";
        Accept(goToDialog);
        await goTo.WaitAsync(TimeSpan.FromSeconds(5));
        Require(vm.CaretLine == 3, "Go To Line moved the editor caret.");

        var missing = vm.LoadFileAsync(file);
        var message = await WaitForDialog<Window>(window);
        Require(message.GetVisualDescendants().OfType<TextBlock>().Any(label =>
            label.Text?.StartsWith("Cannot open ", StringComparison.Ordinal) == true), "File error dialog displays its message.");
        Accept(message);
        Require(!await missing.WaitAsync(TimeSpan.FromSeconds(5)), "File error did not replace the open document.");
        return "Native AOT smoke passed: blank startup, themes, Encoding, line-ending conversion, classic Go To Line availability, file error dialog, automation value." +
            Environment.NewLine + (usesMica ? "Mica menu backdrop and solid editor surfaces." : "Solid window fallback and editor surfaces.") +
            Environment.NewLine + $"First focused blank window: {startupMilliseconds:F0} ms after process start, {focusedMiB:F1} MiB working set.";
    }

    private static async Task<T> WaitForDialog<T>(Window owner) where T : Window
    {
        for (int i = 0; i < 250; i++)
        {
            if (owner.OwnedWindows.OfType<T>().FirstOrDefault(window => window.IsVisible) is { } dialog)
                return dialog;
            await Task.Delay(20);
        }
        throw new TimeoutException($"The {typeof(T).Name} did not open.");
    }

    private static void Accept(Window window)
    {
        window.UpdateLayout();
        var button = window.GetVisualDescendants().OfType<Button>().Single(control => control.IsDefault);
        if (button.Parent is StackPanel row && row.Classes.Contains("dialog-actions"))
        {
            Require(row.Orientation == Orientation.Horizontal && row.HorizontalAlignment == HorizontalAlignment.Right,
                "Dialog actions form a trailing horizontal row.");
            foreach (var action in row.Children.OfType<Button>().Where(action => action.IsVisible))
                Require(action.HorizontalContentAlignment == HorizontalAlignment.Center &&
                    action.VerticalContentAlignment == VerticalAlignment.Center &&
                    action.Bounds.Width >= 88 && action.Bounds.Height >= 32 &&
                    action.Bounds.Right <= row.Bounds.Width,
                    "Dialog buttons are centered and fit in the action row.");
        }
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
