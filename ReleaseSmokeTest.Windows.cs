using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using NotepadAvalonia.Models;
using NotepadAvalonia.Services;
using NotepadAvalonia.Views;
using Notepad.WindowsAutomation;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace AvaloniaNotePad;

internal static partial class ReleaseSmokeTest
{
    [SupportedOSPlatform("windows")]
    private static async Task<string> ExerciseWindowsAsync(MainWindow window, string directory)
    {
        nint hwnd = window.TryGetPlatformHandle()!.Handle;
        var editor = window.ViewModel.EditorControl!;
        window.Activate();
        editor.Focus();
        VerifyWindowsTextInput(window, hwnd);
        editor.LoadDocument("first word\r\nsecond line");
        editor.SetSelection(6, 4);
        nint editorWindow = editor.WindowsAutomationHandle;
        Require(editorWindow != 0, "The native text automation host was created.");
        var automationRoot = ControlAutomationPeer.CreatePeerForElement(window) as IRootProvider;
        var textPeer = editor.GetHostedTextPeer();
        Require(textPeer != null && ReferenceEquals(automationRoot?.GetFocus(), textPeer),
            "The Avalonia automation root routes editor focus to the native text host.");
        var editorPoint = editor.TranslatePoint(new Point(12, 12), window);
        Require(editorPoint.HasValue && ReferenceEquals(automationRoot?.GetPeerFromPoint(editorPoint.Value), textPeer),
            "The Avalonia automation root routes editor hit testing to the native text host.");
        string textAutomation = await Task.Run(() =>
            AutomationProbe.Verify(hwnd, editorWindow, "first word\r\nsecond line")).WaitAsync(TimeSpan.FromSeconds(10));
        WindowsRecentDocuments.VerifyLink(Path.Combine(directory, "Jump List document.txt"));
        string linkedCheck = await VerifyLinkedSaveAsync(window, directory);
        string printCheck = await VerifyPdfSpoolAsync(directory);
        VerifyImeRequests(window, hwnd);
        VerifySessionEnd(window);
        return "Windows smoke passed: native text input, IME cancellation, geometry, reconversion, document feed, " +
            "Jump List link construction, and sign-out with draft recovery." + Environment.NewLine +
            textAutomation + Environment.NewLine + linkedCheck + Environment.NewLine + printCheck;
    }

    private static async Task<string> VerifyLinkedSaveAsync(MainWindow window, string directory)
    {
        string folder = Path.Combine(directory, $"linked-save-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "original.txt");
        string alias = Path.Combine(folder, "alias.txt");
        await File.WriteAllTextAsync(path, "before");
        if (!CreateHardLinkW(alias, path, 0))
        {
            int error = Marshal.GetLastWin32Error();
            if (error is 1 or 50) return "Hard-link save skipped because the verification volume does not support hard links.";
            throw new IOException($"The verification hard link could not be created ({error}).");
        }
        var vm = window.ViewModel;
        Require(await vm.LoadFileAsync(path), "The hard-linked test file opened.");
        vm.EditorControl!.ReplaceRange(0, 6, "after");
        var save = vm.SaveFileCommand.ExecuteAsync(null);
        var dialog = await WaitForDialog<Window>(window);
        Require(dialog.Title == "Save hard-linked file", "The hard-linked file opened its save prompt.");
        var buttons = dialog.GetVisualDescendants().OfType<Button>().ToArray();
        Require(buttons.Length == 3 && buttons.Single(button => button.IsDefault).IsCancel,
            "The hard-link dialog offers three choices with Cancel as the default.");
        Require(await File.ReadAllTextAsync(path) == "before" && await File.ReadAllTextAsync(alias) == "before",
            "While the prompt is open both hard-link names retain the original data.");
        buttons.Single(button => AutomationProperties.GetAutomationId(button) == "MessageDialogUpdateAllLinks")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await save.WaitAsync(TimeSpan.FromSeconds(5));
        Require(await File.ReadAllTextAsync(path) == "after" && await File.ReadAllTextAsync(alias) == "after" &&
            !vm.Document.IsModified && !Directory.EnumerateFiles(folder, ".notepad-recovery-*.bak").Any(),
            "Successful in-place save updates both names, marks the document saved, and removes its recovery copy.");
        await vm.NewFileCommand.ExecuteAsync(null);
        return "The hard-link save dialog updated both links and removed the recovery copy after success.";
    }

    [SupportedOSPlatform("windows")]
    private static async Task<string> VerifyPdfSpoolAsync(string directory)
    {
        string text = string.Join("\r\n", Enumerable.Range(1, 120).Select(index => $"Page sample {index}"));
        foreach (bool landscape in new[] { false, true })
        {
            string path = Path.Combine(directory, $"print-{Guid.NewGuid():N}.pdf");
            if (!await Task.Run(() => WindowsPrinting.PrintVerificationPage(path, text, new()
                { Paper = PaperSize.Letter, Landscape = landscape })).WaitAsync(TimeSpan.FromSeconds(25)))
                return "PDF spool skipped because the Microsoft Print to PDF queue is unavailable.";

            string? pdf = null;
            for (int attempt = 0; pdf == null && attempt < 200; attempt++)
            {
                try
                {
                    if (File.Exists(path))
                    {
                        string content = Encoding.Latin1.GetString(await File.ReadAllBytesAsync(path));
                        if (content.TrimEnd().EndsWith("%%EOF", StringComparison.Ordinal)) pdf = content;
                    }
                }
                catch (IOException) { /* The spooler is still writing the output file. */ }
                if (pdf == null) await Task.Delay(50);
            }
            Require(pdf != null && pdf.StartsWith("%PDF-", StringComparison.Ordinal), "The spooler did not produce a complete PDF.");
            var pages = Regex.Matches(pdf!, @"/Type\s*/Page\b");
            Require(pages.Count > 1, "The print driver did not produce multiple pages.");
            var media = Regex.Match(pdf!, @"/MediaBox\s*\[\s*0(?:\.0+)?\s+0(?:\.0+)?\s+([0-9.]+)\s+([0-9.]+)\s*\]");
            Require(media.Success, "The PDF did not expose the expected page dimensions.");
            double width = double.Parse(media.Groups[1].Value, CultureInfo.InvariantCulture);
            double height = double.Parse(media.Groups[2].Value, CultureInfo.InvariantCulture);
            Require(Math.Abs(width - (landscape ? 792 : 612)) <= 1 &&
                Math.Abs(height - (landscape ? 612 : 792)) <= 1,
                $"Unexpected PDF media box {width} by {height}.");
        }
        return "Microsoft Print to PDF produced multi-page Letter PDFs in portrait and landscape.";
    }

    private static void VerifyWindowsTextInput(MainWindow window, nint hwnd)
    {
        const uint characterMessage = 0x0102; // WM_CHAR
        const uint keyDown = 0x0100;
        const uint keyUp = 0x0101;
        var editor = window.ViewModel.EditorControl!;
        editor.LoadDocument("line\r\nreplace!");
        editor.SetSelection(13, -7);
        SendMessageW(hwnd, keyUp, 0xE7, unchecked((nint)0xC0000001)); // VK_PACKET
        Require(editor.IsFocused, "The editor has focus before native keyboard input.");
        SendMessageW(hwnd, characterMessage, '\uD83D', 1);
        Require(editor.GetSelectedText() == "replace" && !editor.IsModified, "A partial character leaves the selection and undo history unchanged.");
        SendMessageW(hwnd, characterMessage, '\uDE00', 1);
        Require(editor.Text == "line\r\n\U0001F600!" && editor.CaretOffset == 8,
            $"Two native WM_CHAR messages insert one supplementary character. Length={editor.TextLength}, caret={editor.CaretOffset}, focused={editor.IsFocused}.");
        Require(window.ViewModel.CaretLine == 2 && window.ViewModel.CaretColumn == 3,
            "Native input updates the logical line and column.");
        editor.Undo();
        Require(editor.GetSelectedText() == "replace" && editor.CaretOffset == 6 && !editor.IsModified,
            "One undo restores the old text and the backwards selection.");
        editor.Redo();
        SendMessageW(hwnd, keyDown, 8, 0x000E0001); // VK_BACK, make code 0x0E
        SendMessageW(hwnd, keyUp, 8, unchecked((nint)0xC00E0001));
        SendMessageW(hwnd, characterMessage, 'x', 1);
        Require(editor.Text == "line\r\nx!", "Native Backspace removes the whole character, and ordinary typing still works.");
    }

    private static void VerifyImeRequests(MainWindow window, nint hwnd)
    {
        var editor = window.ViewModel.EditorControl!;
        editor.Focus();
        const string surrounding = "prefix \u65e5\u672c suffix";
        editor.LoadDocument(surrounding);
        editor.SetSelection(9, -2);
        SendMessageW(hwnd, 0x010D, 0, 0); // WM_IME_STARTCOMPOSITION
        SendMessageW(hwnd, 0x010E, 0, 0); // WM_IME_ENDCOMPOSITION without a committed result
        Require(editor.Text == surrounding && editor.SelectionAnchor == 9 && editor.CaretOffset == 7 && !editor.IsModified,
            "Cancelling a native composition preserves the selected text and its direction.");
        var request = new TextInputMethodClientRequestedEventArgs { RoutedEvent = InputElement.TextInputMethodClientRequestedEvent };
        editor.RaiseEvent(request);
        var client = request.Client!;
        client.SetPreeditText("\u65e5\u672c", 1);
        const uint imeRequest = 0x0288;
        nint position = Marshal.AllocHGlobal(9 * sizeof(int));
        try
        {
            Marshal.WriteInt32(position, 9 * sizeof(int));
            Marshal.WriteInt32(position, sizeof(int), 1);
            Require(SendMessageW(hwnd, imeRequest, 6, position) == 1, "The native IME query did not accept the composition offset.");
            int[] values = new int[9];
            Marshal.Copy(position, values, 0, values.Length);
            Require(values[4] > 0 && values[2] >= values[5] && values[2] < values[7] &&
                values[3] >= values[6] && values[3] < values[8], "The native IME query did not receive an on-screen caret.");
            Marshal.WriteInt32(position, sizeof(int), 3);
            Require(SendMessageW(hwnd, imeRequest, 6, position) == 0, "The IME accepted an out-of-range composition offset.");
        }
        finally { Marshal.FreeHGlobal(position); }
        client.SetPreeditText(null);

        int bytes = (int)SendMessageW(hwnd, imeRequest, 4, 0);
        Require(bytes == 8 * sizeof(int) + 3 * sizeof(char), "The native reconversion size did not include the selected text.");
        nint data = Marshal.AllocHGlobal(bytes);
        try
        {
            Marshal.WriteInt32(data, bytes);
            Require(SendMessageW(hwnd, imeRequest, 4, data) == bytes, "The reconversion request did not fill the buffer.");
            int[] header = new int[8];
            Marshal.Copy(data, header, 0, header.Length);
            Require(header[4] == 2 && Marshal.PtrToStringUni(data + header[3], header[2]) == "\u65e5\u672c" &&
                editor.SelectionLength == 2, "The IME reconversion request changed the selection or returned the wrong text.");
        }
        finally { Marshal.FreeHGlobal(data); }

        const int documentFeed = 7;
        int feedBytes = (int)SendMessageW(hwnd, imeRequest, documentFeed, 0);
        Require(feedBytes == 8 * sizeof(int) + (surrounding.Length + 1) * sizeof(char),
            "The IME document feed reports the surrounding-text buffer size.");
        nint feed = Marshal.AllocHGlobal(feedBytes);
        try
        {
            Marshal.WriteInt32(feed, feedBytes - 1);
            Require(SendMessageW(hwnd, imeRequest, documentFeed, feed) == 0 && Marshal.ReadInt32(feed) == feedBytes - 1,
                "The IME document feed leaves an undersized buffer untouched.");
            Marshal.WriteInt32(feed, feedBytes);
            Require(SendMessageW(hwnd, imeRequest, documentFeed, feed) == feedBytes, "The IME received surrounding text.");
            int[] header = new int[8];
            Marshal.Copy(feed, header, 0, header.Length);
            Require(Marshal.PtrToStringUni(feed + header[3], header[2]) == surrounding &&
                header[4] == 2 && header[5] == 7 * sizeof(char) && header[6] == 2 && header[7] == header[5] &&
                editor.SelectionAnchor == 9 && editor.CaretOffset == 7 && !editor.IsModified,
                "The IME document feed uses byte offsets and preserves editor state.");
        }
        finally { Marshal.FreeHGlobal(feed); }
    }

    private static void VerifySessionEnd(MainWindow window)
    {
        nint dispatcher = WindowsSessionEnd.FindDispatcherWindow();
        Require(dispatcher != 0, "The Win32 session message window was unavailable.");
        var editor = window.ViewModel.EditorControl!;
        editor.LoadDocument("");
        editor.InsertAtCaret("before sign-out");
        nint logoff = unchecked((nint)0x80000000);
        Require(SendMessageW(dispatcher, WindowsSessionEnd.Query, 0, logoff) == 1 && window.IsVisible,
            "The shutdown query did not checkpoint and leave the editor open.");
        SendMessageW(dispatcher, WindowsSessionEnd.End, 0, logoff);
        Require(window.IsVisible && editor.IsModified, "Cancelled sign-out closed the edited document.");

        editor.InsertAtCaret(" after cancellation");
        Require(SendMessageW(dispatcher, WindowsSessionEnd.Query, 0, logoff) == 1, "A second sign-out query was rejected.");
        var lease = window.ViewModel.Document.Draft!;
        editor.InsertAtCaret(" before confirmation");
        SendMessageW(dispatcher, WindowsSessionEnd.End, 1, logoff);
        Require(!window.IsVisible && lease.Read()?.Content == "before sign-out after cancellation before confirmation",
            "Confirmed sign-out did not preserve the latest draft.");
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint SendMessageW(nint window, uint message, nint wParam, nint lParam);

    [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLinkW(string link, string existing, nint security);
}
