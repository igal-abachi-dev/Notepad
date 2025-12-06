using System.IO;
using System.Text;

namespace NotepadAvalonia.Models;

/// <summary>
/// Represents a document's state and metadata
/// Maps to: g380 (filename), g148 (untitled), g311 (encoding)
/// </summary>
public class DocumentModel
{
    public string FilePath { get; set; } = string.Empty;
    public bool IsUntitled { get; set; } = true;
    public bool IsModified { get; set; } = false;
    public Encoding Encoding { get; set; } = Encoding.Default;
    public FileEncodingType EncodingType { get; set; } = FileEncodingType.ANSI;
    public FileEncodingType SaveEncodingType { get; set; } = FileEncodingType.ANSI;
    public LineEndingStyle LineEnding { get; set; } = LineEndingStyle.CRLF;

    public string FileName => IsUntitled
        ? "Untitled"
        : Path.GetFileName(FilePath);

    public string WindowTitle => $"{(IsModified ? "*" : "")}{FileName} - Notepad";
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
    UTF8 = 5         // UTF-8 without BOM
}
