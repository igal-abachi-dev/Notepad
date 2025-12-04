using System.IO;
using System.Text;
using System.Threading.Tasks;
using NotepadAvalonia.Models;
using NotepadAvalonia.Services;
using Xunit;

namespace Notepad.Tests;

public class FileServiceTests
{
    [Fact]
    public void DetectEncoding_ReturnsUtf8WithBom()
    {
        var service = new FileService();
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF, 0x41 };

        var encoding = service.DetectEncoding(bytes);

        Assert.IsType<UTF8Encoding>(encoding);
        Assert.True(((UTF8Encoding)encoding).GetPreamble().Length > 0);
    }

    [Fact]
    public async Task SaveFileAsync_RespectsRequestedLineEnding()
    {
        var service = new FileService();
        var tempFile = Path.GetTempFileName();

        try
        {
            const string content = "one\nTwo";
            await service.SaveFileAsync(tempFile, content, new UTF8Encoding(false), LineEndingStyle.LF);

            var (loadedContent, _, detectedEnding) = await service.LoadFileAsync(tempFile);

            Assert.Equal("one\nTwo", loadedContent);
            Assert.Equal(LineEndingStyle.LF, detectedEnding);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }
}
