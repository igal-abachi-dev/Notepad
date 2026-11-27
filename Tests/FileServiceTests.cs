using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using NotepadAvalonia.Models;
using NotepadAvalonia.Services;
using Xunit;

namespace Notepad.Tests;

public class FileServiceTests
{
    [Fact]
    public async Task Open_WindowsStyleSlashesAndASpaceInTheName_ReadTheExistingFile()
    {
        using var file = new TemporaryFile();
        string path = Path.Combine(Path.GetDirectoryName(file.Path)!, "Script é.ps1");
        await File.WriteAllTextAsync(path, "Write-Output 1");
        var opened = await new FileService().ReadDocumentAsync(path.Replace('\\', '/'));
        Assert.Equal("Write-Output 1", opened.Content);
    }

    [Fact]
    public void DetectEncoding_AsciiAndInvalidUtf8_DoNotUseLegacyStatisticalGuesses()
    {
        var service = new FileService();
        Assert.Equal(FileEncodingType.UTF8, service.DetectEncodingType("Bush hid the facts"u8.ToArray()));
        Assert.Equal(FileEncodingType.UTF8, service.DetectEncodingType([65, 0]));
        Assert.Equal(FileEncodingType.ANSI, service.DetectEncodingType([0xE9, 0x20, 0x61, 0x61]));
        Assert.Equal(FileEncodingType.ANSI, service.DetectEncodingType([65, 0, 66, 0, 67, 0, 0, 0xD8]));
    }

    [Fact]
    public async Task BomlessUtf16WithoutByteAlignmentClues_CanBeOpenedExplicitly()
    {
        using var file = new TemporaryFile();
        await File.WriteAllBytesAsync(file.Path, Convert.FromHexString("4F60597D"));
        var service = new FileService();
        Assert.Equal("O`Y}", (await service.ReadDocumentAsync(file.Path)).Content);
        Assert.Equal("\u4F60\u597D", (await service.ReadDocumentAsync(file.Path, FileEncodingType.UTF16BE)).Content);
    }

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

    [Theory]
    [InlineData("EFBBBF4100C342", FileEncodingType.UTF8BOM, "A\0\uFFFDB")]
    [InlineData("FFFE410042", FileEncodingType.UTF16LE, "A\uFFFD")]
    [InlineData("FEFF004100", FileEncodingType.UTF16BE, "A\uFFFD")]
    public async Task InvalidUnicode_StrictOpenFailsAndExplicitRecoveryReplacesOnlyInvalidBytes(
        string hex, FileEncodingType type, string expected)
    {
        using var file = new TemporaryFile();
        var service = new FileService();
        var original = Convert.FromHexString(hex);
        await File.WriteAllBytesAsync(file.Path, original);
        await Assert.ThrowsAsync<DecoderFallbackException>(() => service.ReadDocumentAsync(file.Path));

        var recovered = await service.ReadDocumentWithReplacementAsync(file.Path, null, windowsOnly: false);
        Assert.Equal(type, recovered.EncodingType);
        Assert.Equal(expected, recovered.Content);
        Assert.Equal(original, await File.ReadAllBytesAsync(file.Path));
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

    [Fact]
    public async Task TwoCommitsAgainstOneOriginalVersion_OnlyOneCanReplaceIt()
    {
        using var file = new TemporaryFile();
        await File.WriteAllTextAsync(file.Path, "original");
        var initial = await new FileService().ReadDocumentAsync(file.Path);
        var a = CommitAsync("window A");
        var b = CommitAsync("window B");
        var results = await Task.WhenAll(a, b);
        string committed = Assert.Single(results, text => text != null)!;
        Assert.Equal(committed, await File.ReadAllTextAsync(file.Path));

        async Task<string?> CommitAsync(string replacement)
        {
            try
            {
                await new FileService().CommitFileAsync(file.Path, replacement, initial.EncodingType, initial.LineEnding, initial.Version);
                return replacement;
            }
            catch (FileChangedException) { return null; }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HardLinkedSave_RequiresApprovalAndUpdatesBothNames(bool saveAs)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var file = new TemporaryFile();
        var service = new FileService();
        await File.WriteAllTextAsync(file.Path, "original text");
        string alias = file.Path + ".link";
        CreateHardLink(alias, file.Path);
        var original = await service.ReadDocumentAsync(file.Path);
        var expected = saveAs ? null : original.Version;

        var pending = await Assert.ThrowsAsync<HardLinkSaveRequiredException>(() =>
            service.CommitFileAsync(file.Path, "new", FileEncodingType.UTF8, LineEndingStyle.CRLF, expected));
        Assert.Equal("original text", await File.ReadAllTextAsync(file.Path));
        Assert.Equal("original text", await File.ReadAllTextAsync(alias));

        var saved = await service.CommitHardLinkedFileAsync(file.Path, "new", FileEncodingType.UTF8, expected, pending.Target);
        Assert.Equal("new", await File.ReadAllTextAsync(file.Path));
        Assert.Equal("new", await File.ReadAllTextAsync(alias));
        Assert.Equal(saved, (await service.ReadDocumentAsync(alias)).Version);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(file.Path)!, ".notepad-*"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HardLinkedSave_RejectsAChangedVersionOrAnIdenticalReplacementFile(bool replaceFile)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var file = new TemporaryFile();
        var service = new FileService();
        await File.WriteAllTextAsync(file.Path, "original");
        string alias = file.Path + ".link";
        CreateHardLink(alias, file.Path);
        var pending = await Assert.ThrowsAsync<HardLinkSaveRequiredException>(() =>
            service.CommitFileAsync(file.Path, "new", FileEncodingType.UTF8, LineEndingStyle.CRLF));

        string current = "external changes";
        if (replaceFile)
        {
            // The replacement has the same contents and its own hard links.
            File.Delete(file.Path);
            current = "original";
            await File.WriteAllTextAsync(file.Path, current);
            CreateHardLink(file.Path + ".other", file.Path);
        }
        else await File.WriteAllTextAsync(alias, current);

        await Assert.ThrowsAsync<FileChangedException>(() =>
            service.CommitHardLinkedFileAsync(file.Path, "new", FileEncodingType.UTF8, null, pending.Target));
        Assert.Equal(current, await File.ReadAllTextAsync(file.Path));
        Assert.Equal(current, await File.ReadAllTextAsync(alias));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(file.Path)!, ".notepad-*"));
    }

    [Fact]
    public void InPlaceWriteFailure_RetainsRecoveryCopyWithTheExactOriginalBytes()
    {
        using var file = new TemporaryFile();
        byte[] original = [0xFF, 0xFE, 0x41, 0x00, 0x0D, 0x00];
        File.WriteAllBytes(file.Path, original);
        using (var target = File.Open(file.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        using (var failing = new FailingCopy())
        {
            var error = Assert.Throws<IOException>(() => FileService.WriteWithRecoveryCopy(target, failing));
            string recovery = Assert.Single(Directory.GetFiles(Path.GetDirectoryName(file.Path)!, ".notepad-recovery-*.bak"));
            Assert.Contains(recovery, error.Message);
            Assert.Equal(original, File.ReadAllBytes(recovery));
        }
        Assert.Equal(0x42, File.ReadAllBytes(file.Path)[0]);
    }

    internal static void CreateHardLink(string link, string existing) =>
        Assert.True(CreateHardLinkW(link, existing, 0), $"CreateHardLinkW failed ({Marshal.GetLastWin32Error()}).");

    [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLinkW(string link, string existing, nint security);

    private sealed class FailingCopy : MemoryStream
    {
        public override void CopyTo(Stream destination, int bufferSize)
        {
            destination.WriteByte(0x42);
            throw new IOException("Injected write failure.");
        }
    }

    private sealed class TemporaryFile : IDisposable
    {
        private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("notepad-file-tests-");
        public string Path => System.IO.Path.Combine(_folder.FullName, "fixture.txt");
        public void Dispose() => _folder.Delete(recursive: true);
    }
}
