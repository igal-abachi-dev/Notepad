using System;
using System.Diagnostics;
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
    public void LargeFilePolicy_WarnsAbove200MiBAndRejectsAt1GiB()
    {
        Assert.False(FileService.RequiresLargeFileConfirmation(200L * 1024 * 1024));
        Assert.True(FileService.RequiresLargeFileConfirmation(200L * 1024 * 1024 + 1));
        Assert.True(FileService.RequiresLargeFileConfirmation(1024L * 1024 * 1024 - 1));
        Assert.Throws<IOException>(() => FileService.RequiresLargeFileConfirmation(1024L * 1024 * 1024));
    }

    [Fact]
    public async Task Open_DecliningALargeFileSkipsDecoding()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        using var file = new TemporaryFile();
        const long length = 201L * 1024 * 1024;
        using (var stream = File.Create(file.Path))
            stream.SetLength(length);
        bool prompted = false;
        var result = await new FileService().ReadDocumentForEditorAsync(file.Path, requestedEncoding: null,
            windowsOnly: false, size =>
            {
                Assert.Equal(length, size);
                prompted = true;
                return Task.FromResult(false);
            });
        Assert.True(prompted);
        Assert.Null(result);
    }

    [Fact]
    public async Task Save_WhenFileBecomesReadOnlyAfterOpen_PreservesTheOriginal()
    {
        using var file = new TemporaryFile();
        var service = new FileService();
        await File.WriteAllTextAsync(file.Path, "original");
        var initial = await service.ReadDocumentAsync(file.Path);
        Assert.False(initial.IsReadOnly);
        var attributes = File.GetAttributes(file.Path);
        File.SetAttributes(file.Path, attributes | FileAttributes.ReadOnly);
        try
        {
            Assert.True((await service.ReadDocumentAsync(file.Path)).IsReadOnly);
            await Assert.ThrowsAsync<ReadOnlyFileException>(() =>
                service.CommitFileAsync(file.Path, "staged", FileEncodingType.UTF8, LineEndingStyle.LF, initial.Version));
            Assert.Equal("original", await File.ReadAllTextAsync(file.Path));
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(file.Path)!, ".notepad-*.tmp"));
        }
        finally { File.SetAttributes(file.Path, attributes); }

        await service.CommitFileAsync(file.Path, "allowed", FileEncodingType.UTF8, LineEndingStyle.LF, initial.Version);
        Assert.Equal("allowed", await File.ReadAllTextAsync(file.Path));
    }

    [Fact]
    public async Task Save_UnixReadOnlySymlinkTargetIsProtected()
    {
        if (!(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())) return;
        using var file = new TemporaryFile();
        string link = Path.Combine(Path.GetDirectoryName(file.Path)!, "link.txt");
        await File.WriteAllTextAsync(file.Path, "original");
        File.SetUnixFileMode(file.Path, UnixFileMode.UserRead);
        File.CreateSymbolicLink(link, Path.GetFileName(file.Path));
        var service = new FileService();

        var original = await service.ReadDocumentAsync(link);
        Assert.True(original.IsReadOnly);
        Assert.True(FileService.IsReadOnlyFile(link));
        await Assert.ThrowsAsync<ReadOnlyFileException>(() =>
            service.CommitFileAsync(link, "new", FileEncodingType.UTF8, LineEndingStyle.LF, original.Version));
        Assert.Equal(Path.GetFileName(file.Path), new FileInfo(link).LinkTarget);
        Assert.Equal("original", await File.ReadAllTextAsync(file.Path));
        Assert.Equal(UnixFileMode.UserRead, File.GetUnixFileMode(file.Path));
    }

    [Fact]
    public async Task Save_UnixPathsKeepLiteralCharactersAndDistinctCase()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var file = new TemporaryFile();
        string folder = Path.GetDirectoryName(file.Path)!;
        string first = Path.Combine(folder, @"Log_12:30\notes.txt ");
        string second = Path.Combine(folder, @"log_12:30\notes.txt ");
        var service = new FileService();
        await service.SaveFileAsync(first, "first", FileEncodingType.UTF8, LineEndingStyle.LF);
        await service.SaveFileAsync(second, "second", FileEncodingType.UTF8, LineEndingStyle.LF);
        Assert.Equal("first", (await service.ReadDocumentAsync(first)).Content);
        Assert.Equal("second", (await service.ReadDocumentAsync(second)).Content);
        Assert.Equal((UnixFileMode)0, File.GetUnixFileMode(first) & ~(UnixFileMode.UserRead | UnixFileMode.UserWrite));
    }

    [Fact]
    public async Task Save_UnixSymbolicLinkUpdatesTargetAndKeepsItsMode()
    {
        if (!(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())) return;
        using var file = new TemporaryFile();
        string link = Path.Combine(Path.GetDirectoryName(file.Path)!, "link.txt");
        const UnixFileMode mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        await File.WriteAllTextAsync(file.Path, "old");
        File.SetUnixFileMode(file.Path, mode);
        File.CreateSymbolicLink(link, Path.GetFileName(file.Path));
        var service = new FileService();
        var original = await service.ReadDocumentAsync(link);

        await service.CommitFileAsync(link, "new", FileEncodingType.UTF8, LineEndingStyle.LF, original.Version);

        Assert.Equal(Path.GetFileName(file.Path), new FileInfo(link).LinkTarget);
        Assert.Equal("new", await File.ReadAllTextAsync(file.Path));
        Assert.Equal(mode, File.GetUnixFileMode(file.Path));
    }

    [Fact]
    public async Task Save_UnixDanglingAndCyclicSymlinksStayIntact()
    {
        if (!(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())) return;
        using var file = new TemporaryFile();
        string folder = Path.GetDirectoryName(file.Path)!;
        string dangling = Path.Combine(folder, "dangling.txt");
        string cycle = Path.Combine(folder, "cycle.txt");
        File.CreateSymbolicLink(dangling, Path.GetFileName(file.Path));
        File.CreateSymbolicLink(cycle, Path.GetFileName(cycle));
        var service = new FileService();

        foreach (string link in new[] { dangling, cycle })
            await Assert.ThrowsAnyAsync<IOException>(() =>
                service.SaveFileAsync(link, "new", FileEncodingType.UTF8, LineEndingStyle.LF));

        Assert.Equal(Path.GetFileName(file.Path), new FileInfo(dangling).LinkTarget);
        Assert.Equal(Path.GetFileName(cycle), new FileInfo(cycle).LinkTarget);
        Assert.False(File.Exists(file.Path));
    }

    [Theory]
    [InlineData("con.txt")]
    [InlineData("LPT\u00B2.txt")]
    [InlineData("fixture.txt:Zone.Identifier")]
    [InlineData(@"\\.\NUL")]
    [InlineData(@"\\?\GLOBALROOT\Device\Null")]
    public void WindowsDocumentPath_RejectsDevicePathsAndStreams(string path)
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.Throws<IOException>(() => DocumentPath.GetFullPath(path));
    }

    [Fact]
    public async Task WindowsSaveAndOpen_HandlePathsLongerThanMaxPath()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var file = new TemporaryFile();
        string directory = Path.Combine(Path.GetDirectoryName(file.Path)!, new string('a', 110), new string('b', 110));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, new string('c', 40) + ".txt");
        Assert.True(path.Length > 260);
        var service = new FileService();
        await service.SaveFileAsync(path, "first", FileEncodingType.UTF8, LineEndingStyle.CRLF);
        var original = await service.ReadDocumentAsync(path);
        await service.CommitFileAsync(path, "second", FileEncodingType.UTF8, LineEndingStyle.CRLF, original.Version);
        Assert.Equal("second", (await service.ReadDocumentAsync(path)).Content);
    }

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
        {
            Assert.Equal("café", encoding.GetString([0x63, 0x61, 0x66, 0xe9]));
            Assert.Throws<EncoderFallbackException>(() => encoding.GetBytes("\uFF21"));
        }
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
    public async Task GuardedSave_RejectsRetargetedDirectoryAliasWithIdenticalContents()
    {
        if (!(OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())) return;
        using var file = new TemporaryFile();
        string folder = Path.GetDirectoryName(file.Path)!;
        string first = Directory.CreateDirectory(Path.Combine(folder, "first")).FullName;
        string second = Directory.CreateDirectory(Path.Combine(folder, "second")).FullName;
        string alias = Path.Combine(folder, "alias");
        string openedPath = Path.Combine(alias, "file.txt");
        string firstFile = Path.Combine(first, "file.txt");
        string secondFile = Path.Combine(second, "file.txt");
        await File.WriteAllTextAsync(firstFile, "before");
        var service = new FileService();
        try
        {
            CreateDirectoryAlias(alias, first);
            var opened = await service.ReadDocumentAsync(openedPath);
            var saved = await service.CommitFileAsync(openedPath, "same bytes", opened.EncodingType, opened.LineEnding, opened.Version);
            Assert.Equal("same bytes", await File.ReadAllTextAsync(firstFile));
            Assert.NotNull(saved.ResolvedPath);
            Assert.Equal(opened.Version.ResolvedPath, saved.ResolvedPath);

            await File.WriteAllTextAsync(secondFile, "same bytes");
            Directory.Delete(alias);
            CreateDirectoryAlias(alias, second);
            await Assert.ThrowsAsync<FileChangedException>(() =>
                service.CommitFileAsync(openedPath, "edit", opened.EncodingType, opened.LineEnding, saved));
            Assert.Equal("same bytes", await File.ReadAllTextAsync(firstFile));
            Assert.Equal("same bytes", await File.ReadAllTextAsync(secondFile));
            Assert.Empty(Directory.GetFiles(second, "*.tmp"));
        }
        finally
        {
            if (Directory.Exists(alias)) Directory.Delete(alias);
        }
    }

    [Fact]
    public async Task GuardedSave_RejectsRetargetedFileSymlinkAndProtectsLegacyVersions()
    {
        if (!(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())) return;
        using var file = new TemporaryFile();
        string folder = Path.GetDirectoryName(file.Path)!;
        string other = Path.Combine(folder, "second.txt");
        string link = Path.Combine(folder, "link.txt");
        await File.WriteAllTextAsync(file.Path, "same bytes");
        await File.WriteAllTextAsync(other, "same bytes");
        File.CreateSymbolicLink(link, Path.GetFileName(file.Path));
        var service = new FileService();
        var opened = await service.ReadDocumentAsync(link);
        File.Delete(link);
        File.CreateSymbolicLink(link, Path.GetFileName(other));

        await Assert.ThrowsAsync<FileChangedException>(() =>
            service.CommitFileAsync(link, "edit", opened.EncodingType, opened.LineEnding, opened.Version));
        var legacy = opened.Version with { ResolvedPath = null };
        await Assert.ThrowsAsync<FileChangedException>(() =>
            service.CommitFileAsync(link, "edit", opened.EncodingType, opened.LineEnding, legacy));
        Assert.Equal("same bytes", await File.ReadAllTextAsync(file.Path));
        Assert.Equal("same bytes", await File.ReadAllTextAsync(other));
        Assert.Equal(Path.GetFileName(other), new FileInfo(link).LinkTarget);

        // Legacy drafts for direct paths can still save when the contents agree.
        string directPath = opened.Version.ResolvedPath!;
        await service.CommitFileAsync(directPath, "edit", opened.EncodingType, opened.LineEnding, legacy);
        Assert.Equal("edit", await File.ReadAllTextAsync(file.Path));
    }

    private static void CreateDirectoryAlias(string alias, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(alias, target);
            return;
        }

        var command = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        foreach (string argument in new[] { "/d", "/c", "mklink", "/J", alias, target }) command.ArgumentList.Add(argument);
        using var process = Process.Start(command)!;
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, output);
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
        if (!(OperatingSystem.IsWindows() || OperatingSystem.IsLinux())) return;
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
        if (!(OperatingSystem.IsWindows() || OperatingSystem.IsLinux())) return;
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
    public async Task HardLinkedSave_ConcurrentApprovalsThroughDifferentNamesOnlyCommitOnce()
    {
        if (!(OperatingSystem.IsWindows() || OperatingSystem.IsLinux())) return;
        using var file = new TemporaryFile();
        var service = new FileService();
        await File.WriteAllTextAsync(file.Path, "original");
        string alias = file.Path + ".link";
        CreateHardLink(alias, file.Path);
        var first = await ApproveAsync(file.Path);
        var second = await ApproveAsync(alias);
        var results = await Task.WhenAll(CommitAsync(file.Path, "first", first), CommitAsync(alias, "second", second));
        string committed = Assert.Single(results, text => text != null)!;
        Assert.Equal(committed, await File.ReadAllTextAsync(file.Path));
        Assert.Equal(committed, await File.ReadAllTextAsync(alias));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(file.Path)!, ".notepad-*"));

        async Task<HardLinkSaveTarget> ApproveAsync(string path)
        {
            var prompt = await Assert.ThrowsAsync<HardLinkSaveRequiredException>(() =>
                service.CommitFileAsync(path, "edit", FileEncodingType.UTF8, LineEndingStyle.LF));
            return prompt.Target;
        }

        async Task<string?> CommitAsync(string path, string content, HardLinkSaveTarget target)
        {
            try
            {
                await service.CommitHardLinkedFileAsync(path, content, FileEncodingType.UTF8, null, target);
                return content;
            }
            catch (IOException) { return null; }
        }
    }

    [Fact]
    public void InPlaceWriteFailure_RetainsRecoveryCopyWithTheExactOriginalBytes()
    {
        using var file = new TemporaryFile();
        byte[] original = [0xFF, 0xFE, 0x41, 0x00, 0x0D, 0x00];
        File.WriteAllBytes(file.Path, original);
        string alias = file.Path + ".link";
        bool supportsLinks = OperatingSystem.IsWindows() || OperatingSystem.IsLinux();
        if (supportsLinks) CreateHardLink(alias, file.Path);
        using (var target = File.Open(file.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        using (var failing = new FailingCopy())
        {
            var error = Assert.Throws<IOException>(() => FileService.WriteWithRecoveryCopy(target, failing));
            string recovery = Assert.Single(Directory.GetFiles(Path.GetDirectoryName(file.Path)!, ".notepad-recovery-*.bak"));
            Assert.Contains(recovery, error.Message);
            Assert.Equal(original, File.ReadAllBytes(recovery));
            if (OperatingSystem.IsLinux())
            {
                const UnixFileMode groupAndOther = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;
                Assert.Equal((UnixFileMode)0, File.GetUnixFileMode(recovery) & groupAndOther);
            }
        }
        Assert.Equal(0x42, File.ReadAllBytes(file.Path)[0]);
        if (supportsLinks) Assert.Equal(File.ReadAllBytes(file.Path), File.ReadAllBytes(alias));
    }

    internal static void CreateHardLink(string link, string existing)
    {
        bool created = OperatingSystem.IsWindows() ? CreateHardLinkW(link, existing, 0) : CreateLinuxHardLink(existing, link) == 0;
        Assert.True(created, $"Hard-link creation failed ({Marshal.GetLastPInvokeError()}).");
    }

    [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLinkW(string link, string existing, nint security);

    [DllImport("libc", EntryPoint = "link", SetLastError = true)]
    private static extern int CreateLinuxHardLink([MarshalAs(UnmanagedType.LPUTF8Str)] string existing,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string link);

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
