using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using NotepadAvalonia.Models;
using NotepadAvalonia.Views;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace AvaloniaNotePad;

internal static class ReleaseSmokeTest
{
    public static async Task RunAsync(IClassicDesktopStyleApplicationLifetime desktop, MainWindow window, string reportPath)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
            Require(!System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported, "The running executable is Native AOT.");
            var log = await ExerciseDialogsAsync(window, Path.GetDirectoryName(reportPath)!);
            File.WriteAllText(reportPath, log);
            desktop.Shutdown();
        }
        catch (Exception ex)
        {
            File.WriteAllText(reportPath, ex.ToString());
            desktop.Shutdown(1);
        }
    }

    private static async Task<string> ExerciseDialogsAsync(MainWindow window, string directory)
    {
        var vm = window.ViewModel;
        Require(vm.EditorControl?.IsFocused == true && vm.EditorControl.TextLength == 0, "The blank editor has focus.");
        using var process = Process.GetCurrentProcess();
        double focusedMiB = process.WorkingSet64 / (1024.0 * 1024);
        vm.SetThemeCommand.Execute(EditorTheme.Dark);
        Require(window.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark, "Dark theme applied to the window.");
        vm.SetThemeCommand.Execute(EditorTheme.Light);
        Require(window.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Light, "Light theme applied to the window.");
        var selectEncoding = vm.ChangeDefaultEncodingCommand.ExecuteAsync(null);
        var encodingDialog = await WaitForDialog<EncodingDialog>(window);
        var options = encodingDialog.FindControl<ComboBox>("EncodingCombo")!;
        Require(options.ItemCount == 5 && options.SelectedItem is EncodingDialog.EncodingOption, "Encoding choices rendered.");
        options.SelectedIndex = 2;
        encodingDialog.UpdateLayout();
        Require(options.GetVisualDescendants().OfType<TextBlock>().Any(label => label.Text == "UTF-16 BE"),
            "The selected encoding label displays its name.");
        Accept(encodingDialog);
        await selectEncoding.WaitAsync(TimeSpan.FromSeconds(5));
        Require(vm.Document.SaveEncodingType == FileEncodingType.UTF16BE, "Encoding dialog applied UTF-16 BE.");

        var file = Path.Combine(directory, $"smoke-input-{Guid.NewGuid():N}.txt");
        vm.EditorControl!.LoadDocument("first\r\nsecond\r\nthird");
        var peer = ControlAutomationPeer.CreatePeerForElement(vm.EditorControl);
        Require(peer?.GetProvider<IValueProvider>()?.Value == "first\r\nsecond\r\nthird",
            "The published editor exposes the document through its automation peer.");
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
        return "Native AOT smoke passed: blank startup, themes, Encoding, Go To Line, file error dialog, automation value." +
            Environment.NewLine + $"First focused blank window: {focusedMiB:F1} MiB working set.";
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
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
