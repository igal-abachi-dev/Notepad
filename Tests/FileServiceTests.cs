using System;
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
    public void DetectEncoding_RecognizesFourByteUtf8()
    {
        var service = new FileService();
        Assert.Equal(FileEncodingType.UTF8, service.DetectEncodingType(Encoding.UTF8.GetBytes("hello 😀")));
    }

    [Theory]
    [InlineData(false, FileEncodingType.UTF16LE)]
    [InlineData(true, FileEncodingType.UTF16BE)]
    public void DetectEncoding_RecognizesBomlessUtf16(bool bigEndian, FileEncodingType expected)
    {
        var service = new FileService();
        byte[] bytes = new UnicodeEncoding(bigEndian, byteOrderMark: false).GetBytes("hello");
        Assert.Equal(expected, service.DetectEncodingType(bytes));
    }

    [Fact]
    public void GetEncoding_Ansi_UsesTheLocalCodePage()
    {
        var service = new FileService();
        var encoding = service.GetEncoding(FileEncodingType.ANSI);
        Assert.Equal(OperatingSystem.IsWindows() ? Encoding.GetEncoding(0).CodePage : 1252, encoding.CodePage);
        if (encoding.CodePage == 1252)
            Assert.Equal("café", encoding.GetString([0x63, 0x61, 0x66, 0xe9]));
    }

    [Fact]
    public async Task SaveFileAsync_UnencodableText_PreservesOriginal()
    {
        var service = new FileService();
        string path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "original");
            await Assert.ThrowsAsync<EncoderFallbackException>(() =>
                service.SaveFileAsync(path, "\uD800", FileEncodingType.ANSI, LineEndingStyle.CRLF));
            Assert.Equal("original", await File.ReadAllTextAsync(path));
        }
        finally
        {
            File.Delete(path);
        }
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
