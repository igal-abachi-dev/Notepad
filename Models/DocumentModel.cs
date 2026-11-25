using CommunityToolkit.Mvvm.ComponentModel;
using NotepadAvalonia.Services;
using System.IO;
using System.Text;

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
    public Encoding Encoding { get; set; } = Encoding.Default;
    public FileEncodingType EncodingType { get; set; } = FileEncodingType.UTF8;
    public FileEncodingType SaveEncodingType { get; set; } = FileEncodingType.UTF8;
    public LineEndingStyle LineEnding { get; set; } = LineEndingStyle.CRLF;
    internal FileVersion? SavedFileVersion { get; set; }
    internal DraftLease? Draft { get; set; }
    internal long DraftVersion { get; set; } = -1;
    internal long DraftSequence { get; set; }

    public string FileName => IsUntitled
        ? "Untitled"
        : Path.GetFileName(FilePath);

    public string WindowTitle => $"{(IsModified ? "*" : "")}{FileName} - Notepad";
}

public sealed record FileVersion(long Length, string Sha256);

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
    UTF8 = 5         // UTF-8 without BOM
}
