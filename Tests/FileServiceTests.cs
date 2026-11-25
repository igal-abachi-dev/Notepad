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

    [Theory]
    [InlineData(FileEncodingType.ANSI, "410D0A420A430D")]
    [InlineData(FileEncodingType.UTF8, "410D0A420A430D")]
    [InlineData(FileEncodingType.UTF8BOM, "EFBBBF410D0A420A430D")]
    [InlineData(FileEncodingType.UTF16LE, "FFFE41000D000A0042000A0043000D00")]
    [InlineData(FileEncodingType.UTF16BE, "FEFF0041000D000A0042000A0043000D")]
    public async Task ExplicitEncoding_RoundTripsTheFiveSelectableFormats(FileEncodingType type, string bytes)
    {
        using var file = new TemporaryFile();
        var service = new FileService();
        const string text = "A\r\nB\nC\r";
        await service.SaveFileAsync(file.Path, text, type, LineEndingStyle.CRLF);
        Assert.Equal(Convert.FromHexString(bytes), await File.ReadAllBytesAsync(file.Path));
        var reopened = await service.LoadFileAsync(file.Path, type);
        Assert.Equal(text, reopened.content);
        Assert.Equal(type, reopened.encodingType);
    }

    [Fact]
    public async Task LoadFileAsync_ExplicitAnsiAndWindowsLineEndingPolicyAreIndependent()
    {
        using var file = new TemporaryFile();
        var service = new FileService();
        await File.WriteAllBytesAsync(file.Path, [0xEF, 0xBB, 0xBF, 0x41, 0x00, 0x0A, 0x42, 0x0D]);

        var auto = await service.LoadFileAsync(file.Path);
        Assert.Equal("A\0\nB\r", auto.content);
        var forced = await service.LoadFileAsync(file.Path, FileEncodingType.UTF8, windowsOnly: true);
        Assert.Equal("A\0\r\nB\r\n", forced.content);
        Assert.Equal((FileEncodingType.UTF8, LineEndingStyle.CRLF), (forced.encodingType, forced.lineEnding));
        var ansi = await service.LoadFileAsync(file.Path, FileEncodingType.ANSI);
        Assert.Equal(service.GetEncoding(FileEncodingType.ANSI).GetString(await File.ReadAllBytesAsync(file.Path)), ansi.content);
    }

    [Fact]
    public async Task SaveFileAsync_WindowsSharingViolationRetainsTheOriginalAndCleansStaging()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var file = new TemporaryFile();
        await File.WriteAllTextAsync(file.Path, "previous");
        using (var locked = File.Open(file.Path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await Assert.ThrowsAnyAsync<IOException>(() =>
                new FileService().SaveFileAsync(file.Path, "replacement", FileEncodingType.UTF8, LineEndingStyle.CRLF));
            Assert.Equal("previous", await File.ReadAllTextAsync(file.Path));
            Assert.Equal(file.Path, Assert.Single(Directory.GetFiles(System.IO.Path.GetDirectoryName(file.Path)!)));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GuardedSave_WhenFileChangesOrDisappears_KeepsTheExternalState(bool removed)
    {
        using var file = new TemporaryFile();
        await File.WriteAllTextAsync(file.Path, "before");
        var service = new FileService();
        var opened = await service.ReadDocumentAsync(file.Path);
        if (removed) File.Delete(file.Path);
        else await File.WriteAllTextAsync(file.Path, "edited");

        await Assert.ThrowsAsync<FileChangedException>(() =>
            service.CommitFileAsync(file.Path, "our update", opened.EncodingType, opened.LineEnding, opened.Version));
        Assert.Equal(!removed, File.Exists(file.Path));
        if (!removed) Assert.Equal("edited", await File.ReadAllTextAsync(file.Path));
        Assert.Empty(Directory.GetFiles(System.IO.Path.GetDirectoryName(file.Path)!, "*.tmp"));
    }

    private sealed class TemporaryFile : IDisposable
    {
        private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("notepad-file-tests-");
        public string Path => System.IO.Path.Combine(_folder.FullName, "fixture.txt");
        public void Dispose() => _folder.Delete(recursive: true);
    }
}
