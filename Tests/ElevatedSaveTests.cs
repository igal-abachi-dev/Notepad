using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using NotepadAvalonia.Models;
using NotepadAvalonia.Services;
using Xunit;

namespace Notepad.Tests;

public class ElevatedSaveTests
{
    [Fact]
    public async Task Worker_RejectsATamperedRequestAndSavesTheApprovedBytes()
    {
        var folder = Directory.CreateTempSubdirectory("notepad-admin-save-");
        try
        {
            string path = Path.Combine(folder.FullName, "notes.txt");
            string handoff = Path.Combine(folder.FullName, $"notepad-admin-{Guid.NewGuid():N}.req");
            var files = new FileService();
            await File.WriteAllTextAsync(path, "original");
            var original = await files.ReadDocumentAsync(path);

            string seal;
            using (var request = new FileStream(handoff, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                seal = ElevatedSave.WriteRequest(request, path, "new\0text\n", FileEncodingType.UTF8BOM, LineEndingStyle.LF, original.Version);
            using var readLock = File.OpenRead(handoff);
            Assert.Equal(ElevatedSave.WorkerResult.Failed, await ElevatedSave.CommitRequestAsync(files, handoff, new string('0', 64)));
            Assert.Equal("original", await File.ReadAllTextAsync(path));
            Assert.Equal(ElevatedSave.WorkerResult.Saved, await ElevatedSave.CommitRequestAsync(files, handoff, seal));
            byte[] expected = [.. new UTF8Encoding(true).GetPreamble(), .. "new\0text\n"u8];
            Assert.Equal(expected, await File.ReadAllBytesAsync(path));
            Assert.Equal(ElevatedSave.WorkerResult.Changed, await ElevatedSave.CommitRequestAsync(files, handoff, seal));
        }
        finally { folder.Delete(recursive: true); }
    }
}
