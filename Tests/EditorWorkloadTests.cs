using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Notepad.NeoEdit;
using NotepadAvalonia.Models;
using NotepadAvalonia.Services;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Notepad.Tests;

public class EditorWorkloadTests(ITestOutputHelper output)
{
    [AvaloniaFact]
    public async Task LargeDocument_SnapshotsSearchEditsAndScrollStayConsistent()
    {
        string line = "sample event with a long value ".PadRight(160, 'x');
        string text = string.Concat(Enumerable.Repeat(line + "\r\n", 12_000)) + new string('m', 20_000) + " END";
        var editor = new NeoEditor();
        var window = new Window { Width = 560, Height = 360, Content = editor };
        var folder = Directory.CreateTempSubdirectory("notepad-workload-");
        var process = Process.GetCurrentProcess();
        try
        {
            string path = Path.Combine(folder.FullName, "large.txt");
            var files = new FileService();
            await File.WriteAllTextAsync(path, text);
            var timer = Stopwatch.StartNew();
            var loaded = await files.LoadFileAsync(path);
            output.WriteLine($"Read {text.Length:N0} UTF-16 units: {timer.Elapsed.TotalMilliseconds:F1} ms");

            Measure("Initialize piece tree", () => editor.LoadDocument(loaded.content));
            Measure("First headless layout", () =>
            {
                window.Show();
                window.UpdateLayout();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
            });
            Measure("Navigate to 20,000-character line", () => editor.CaretOffset = text.Length);
            Assert.True(editor.Offset.X > 0);
            Assert.True(editor.Offset.Y > 0);

            Measure("100 edits at the beginning and EOF", () =>
            {
                for (int i = 0; i < 50; i++)
                {
                    editor.ReplaceRange(0, 0, "a", endCaret: 0);
                    editor.ReplaceRange(editor.TextLength, 0, "z");
                }
            });
            string snapshot = "";
            Measure("First unchanged-document snapshot", () => snapshot = editor.GetText());
            Assert.Same(snapshot, editor.GetText());

            var search = new SearchService();
            var query = new SearchSettings { SearchString = " END", MatchCase = true, WrapAround = true };
            Measure("100 repeated searches with a shared snapshot", () =>
            {
                editor.CaretOffset = 0;
                for (int i = 0; i < 100; i++)
                    Assert.Equal(new SearchResult(text.Length + 46, 4), search.FindNext(editor, query).result);
            });

            Measure("Initial wrapped layout", () => editor.WordWrap = true);
            Assert.Equal(editor.Viewport.Width, editor.Extent.Width);
            Assert.Equal(12_001, editor.LineCount);
            var replacement = new SearchSettings { SearchString = "sample", ReplaceString = "event", MatchCase = true };
            Measure("12,000 replacements and one undo", () =>
            {
                Assert.Equal(12_000, search.ReplaceAll(editor, replacement));
                editor.Undo();
            });
            Assert.Equal(snapshot, editor.GetText());

            timer.Restart();
            await files.SaveFileAsync(path, snapshot, FileEncodingType.UTF8, LineEndingStyle.CRLF);
            output.WriteLine($"Staged save: {timer.Elapsed.TotalMilliseconds:F1} ms");
            Assert.Equal(snapshot, (await files.LoadFileAsync(path)).content);
            output.WriteLine($"Test process peak working set: {process.PeakWorkingSet64 / (1024 * 1024)} MiB");
        }
        finally
        {
            process.Dispose();
            window.Close();
            folder.Delete(recursive: true);
        }

        void Measure(string operation, Action action)
        {
            long start = Stopwatch.GetTimestamp();
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            action();
            output.WriteLine($"{operation}: {Stopwatch.GetElapsedTime(start).TotalMilliseconds:F1} ms, " +
                $"{(GC.GetAllocatedBytesForCurrentThread() - allocated) / 1024.0:F0} KiB allocated on UI thread");
        }
    }
}
