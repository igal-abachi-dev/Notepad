using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using NotepadAvalonia.Models;
using NotepadAvalonia.Services;
using NotepadAvalonia.Views;
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
        string printCheck = await VerifyPdfSpoolAsync(directory);
        VerifyImeRequests(window, hwnd);
        VerifySessionEnd(window);
        return "Windows smoke passed: IME geometry and reconversion requests, " +
            "cancelled sign-out and confirmed sign-out with draft recovery." + Environment.NewLine + printCheck;
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

    private static void VerifyImeRequests(MainWindow window, nint hwnd)
    {
        var editor = window.ViewModel.EditorControl!;
        editor.Focus();
        editor.LoadDocument("\u65e5\u672c suffix");
        editor.SetSelection(0, 2);
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
}
