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

        var encoding = service.DetectEncodingType(bytes);

        Assert.Equal(FileEncodingType.UTF8BOM, encoding);
    }

    [Fact]
    public async Task SaveFileAsync_PreservesExistingLineEndings()
    {
        var service = new FileService();
        var tempFile = Path.GetTempFileName();

        try
        {
            const string content = "one\r\ntwo\nthree\r";
            await service.SaveFileAsync(tempFile, content, FileEncodingType.UTF8, LineEndingStyle.CRLF);

            var (loadedContent, _, _, detectedEnding) = await service.LoadFileAsync(tempFile);

            Assert.Equal(content, loadedContent);
            Assert.Equal(LineEndingStyle.CRLF, detectedEnding);
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
