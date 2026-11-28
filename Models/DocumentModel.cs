using CommunityToolkit.Mvvm.ComponentModel;
using NotepadAvalonia.Services;
using System;
using System.IO;
using System.Text;
using System.Text.Json.Serialization;

namespace NotepadAvalonia.Models;

/// <summary>
/// Represents a document's state and metadata
/// Maps to: g380 (filename), g148 (untitled), g311 (encoding)
/// </summary>
public partial class DocumentModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FileName), nameof(WindowTitle))]
    private string _filePath = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FileName), nameof(WindowTitle))]
    private bool _isUntitled = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    private bool _isModified;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    private bool _isReadOnlyFile;

    public Encoding Encoding { get; set; } = Encoding.Default;
    public FileEncodingType EncodingType { get; set; } = FileEncodingType.UTF8;
    public FileEncodingType SaveEncodingType { get; set; } = FileEncodingType.UTF8;
    public LineEndingStyle LineEnding { get; set; } = LineEndingStyle.CRLF;
    internal FileVersion? SavedFileVersion { get; set; }
    internal string? SuggestedExtension { get; init; }
    internal DraftLease? Draft { get; set; }
    internal long DraftVersion { get; set; } = -1;
    internal long DraftSequence { get; set; }

    public string FileName => IsUntitled
        ? "Untitled"
        : Path.GetFileName(FilePath);

    public string WindowTitle => $"{(IsModified ? "*" : "")}{FileName}{(IsReadOnlyFile ? " [Read-only file]" : "")} - Notepad";
}

public sealed record FileVersion(long Length, string Sha256)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ResolvedPath { get; init; }

    // A version can still compare equal through two names for the same bytes. Save checks the destination separately.
    public bool Equals(FileVersion? other) => other != null && Length == other.Length && Sha256 == other.Sha256;
    public override int GetHashCode() => HashCode.Combine(Length, Sha256);
}

public enum LineEndingStyle
{
    CRLF,  // Windows: \r\n
    LF,    // Unix: \n
    CR     // Old Mac: \r
}

/// <summary>
/// Maps to Notepad's encoding detection (g311 values)
/// </summary>
public enum FileEncodingType
{
    ANSI = 1,        // System code page
    UTF16LE = 2,     // UTF-16 Little Endian (with BOM)
    UTF16BE = 3,     // UTF-16 Big Endian (with BOM)
    UTF8BOM = 4,     // UTF-8 with BOM
    UTF8 = 5,      // UTF-8 without BOM
    OEM437 = 6     // DOS / ANSI art
}
