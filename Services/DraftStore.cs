using NotepadAvalonia.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace NotepadAvalonia.Services;

public sealed record DraftSnapshot(string Content, string? Path, FileEncodingType Encoding, LineEndingStyle LineEnding,
    int Anchor, int Caret, FileVersion? SavedFileVersion, DateTimeOffset Updated, string? Preview = null);

public sealed record DraftInfo(Guid Id, string Name, string? Path, DateTimeOffset Updated, string? Preview)
{
    public string DateLabel => Updated.ToLocalTime().ToString("G");
    public string? Detail => Path ?? Preview;
}

internal sealed record DraftEnvelope(int Format, DraftSnapshot Snapshot, string Checksum);

[JsonSerializable(typeof(DraftEnvelope))]
internal partial class DraftJsonContext : JsonSerializerContext;

public sealed class DraftStore(string directory)
{
    public static DraftStore ForCurrentUser() => new(System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NotepadAvalonia", "Drafts"));

    public async Task<IReadOnlyList<DraftInfo>> DiscoverAsync() => await Task.Run(() =>
    {
        if (!Directory.Exists(directory)) return Array.Empty<DraftInfo>();
        var drafts = new List<DraftInfo>();
        var ids = Directory.EnumerateFiles(directory)
            .Where(path => System.IO.Path.GetExtension(path) is ".draft" or ".previous")
            .Select(path => System.IO.Path.GetFileNameWithoutExtension(path))
            .Distinct();
        foreach (string name in ids)
        {
            if (!Guid.TryParseExact(name, "N", out var id)) continue;
            using var lease = TryClaim(id);
            var snapshot = lease?.ReadMetadata();
            if (snapshot != null)
                drafts.Add(new(id, snapshot.Path == null ? "Untitled" : System.IO.Path.GetFileName(snapshot.Path),
                    snapshot.Path, snapshot.Updated, snapshot.Preview));
        }
        return (IReadOnlyList<DraftInfo>)drafts.OrderByDescending(draft => draft.Updated).ToArray();
    }).ConfigureAwait(false);

    public DraftLease Create() => TryClaim(Guid.NewGuid()) ?? throw new IOException("Cannot claim a draft slot.");

    public DraftLease? TryClaim(Guid id)
    {
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(directory);
        else Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var stem = System.IO.Path.Combine(directory, id.ToString("N"));
        try
        {
            // Keep the lock file in place. Unlinking a locked file could allow a second process to lock a different inode.
            return new DraftLease(id, stem, new FileStream(stem + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
        }
        catch (IOException)
        {
            return null;
        }
    }
}

public sealed class DraftLease : IDisposable
{
    private const int FormatVersion = 1;
    private const int MaxHeaderBytes = 256 * 1024;
    private static readonly Encoding DraftEncoding = new UTF8Encoding(false, true);
    private readonly string _stem;
    private readonly FileStream _lockFile;
    private readonly SemaphoreSlim _io = new(1);
    private long _writtenRevision = -1;
    private bool _readFromPrevious;
    private volatile bool _released;

    internal DraftLease(Guid id, string stem, FileStream lockFile)
    {
        Id = id;
        _stem = stem;
        _lockFile = lockFile;
    }

    public Guid Id { get; }
    private string Current => _stem + ".draft";
    private string Previous => _stem + ".previous";

    public DraftSnapshot? Read()
    {
        var current = ReadFile(Current, metadataOnly: false);
        _readFromPrevious = current == null;
        return current ?? ReadFile(Previous, metadataOnly: false);
    }

    internal DraftSnapshot? ReadMetadata() =>
        ReadFile(Current, metadataOnly: true) ?? ReadFile(Previous, metadataOnly: true);

    public Task WriteAsync(DraftSnapshot snapshot, long revision) => RunIoAsync(() =>
    {
        if (_writtenRevision >= revision) return;
        snapshot = snapshot with { Preview = MakePreview(snapshot.Content) };
        var envelope = new DraftEnvelope(FormatVersion, snapshot with { Content = "" }, Checksum(snapshot));
        var header = JsonSerializer.SerializeToUtf8Bytes(envelope, DraftJsonContext.Default.DraftEnvelope);
        if (header.Length > MaxHeaderBytes) throw new IOException("The draft metadata is too large.");
        var staging = _stem + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var stream = new FileStream(staging, options))
            {
                using (var writer = new BinaryWriter(stream, DraftEncoding, leaveOpen: true))
                {
                    writer.Write(header.Length);
                    writer.Write(header);
                }
                using (var writer = new StreamWriter(stream, DraftEncoding, bufferSize: 8192, leaveOpen: true))
                    writer.Write(snapshot.Content.AsSpan());
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(Current))
            {
                // Preserve a good fallback if the active copy was found corrupt during recovery.
                if (_readFromPrevious && File.Exists(Previous))
                    File.Move(staging, Current, overwrite: true);
                else
                    File.Replace(staging, Current, Previous);
            }
            else
                File.Move(staging, Current);
            _writtenRevision = revision;
            _readFromPrevious = false;
        }
        finally
        {
            File.Delete(staging);
        }
    });

    public Task DiscardAsync() => RunIoAsync(() =>
    {
        File.Delete(Previous);
        File.Delete(Current);
    });

    public Task ReleaseAsync() => RunIoAsync(Dispose);

    private async Task RunIoAsync(Action operation)
    {
        await _io.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_released)
                await Task.Run(operation).ConfigureAwait(false);
        }
        finally
        {
            _io.Release();
        }
    }

    private static DraftSnapshot? ReadFile(string path, bool metadataOnly)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read);
            using var reader = new BinaryReader(stream, DraftEncoding, leaveOpen: true);
            int length = reader.ReadInt32();
            if (length <= 0 || length > MaxHeaderBytes || length > stream.Length - stream.Position) return null;
            var envelope = JsonSerializer.Deserialize(reader.ReadBytes(length), DraftJsonContext.Default.DraftEnvelope);
            if (envelope is { Format: FormatVersion, Snapshot.Content: "" } &&
                Enum.IsDefined(envelope.Snapshot.Encoding) && Enum.IsDefined(envelope.Snapshot.LineEnding) &&
                (envelope.Snapshot.Path == null || System.IO.Path.IsPathFullyQualified(envelope.Snapshot.Path)))
            {
                if (metadataOnly) return envelope.Snapshot;
                using var payload = new StreamReader(stream, DraftEncoding, detectEncodingFromByteOrderMarks: false);
                var snapshot = envelope.Snapshot with { Content = payload.ReadToEnd() };
                if (StringComparer.Ordinal.Equals(envelope.Checksum, Checksum(snapshot)))
                    return snapshot;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or DecoderFallbackException)
        {
            // The previous generation may still be readable.
        }
        return null;
    }

    private static string Checksum(DraftSnapshot snapshot)
    {
        // Include metadata so damaged paths and encodings never drive a recovered save.
        var checksumInput = JsonSerializer.SerializeToUtf8Bytes(snapshot with { Content = "" }, DraftJsonContext.Default.DraftSnapshot);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(checksumInput);
        hash.AppendData(MemoryMarshal.AsBytes(snapshot.Content.AsSpan()));
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static string? MakePreview(string content)
    {
        var excerpt = content.AsSpan(0, Math.Min(content.Length, 120)).Trim();
        int lineEnd = excerpt.IndexOfAny('\r', '\n');
        if (lineEnd >= 0) excerpt = excerpt[..lineEnd];
        if (!excerpt.IsEmpty && char.IsHighSurrogate(excerpt[^1])) excerpt = excerpt[..^1];
        return excerpt.IsEmpty ? null : excerpt.ToString().Replace('\0', ' ').Replace('\t', ' ');
    }

    public void Dispose()
    {
        _released = true;
        _lockFile.Dispose();
    }
}
