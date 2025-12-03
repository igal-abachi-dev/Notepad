using System;
using System.IO;
using System.Text;

namespace AvaloniaNotePad.Models;

/// <summary>
/// Represents a text document with metadata
/// </summary>
public class Document
{
    public string? FilePath { get; set; }
    public string Content { get; set; } = string.Empty;
    public Encoding Encoding { get; set; } = Encoding.UTF8;
    public LineEnding LineEnding { get; set; } = LineEnding.CRLF;
    public bool IsModified { get; set; }
    public bool IsNewDocument => string.IsNullOrEmpty(FilePath);
    public DateTime? LastModified { get; set; }
    
    public string FileName => string.IsNullOrEmpty(FilePath) 
        ? "Untitled" 
        : Path.GetFileName(FilePath);
        
    public string DisplayName => IsModified ? $"{FileName} *" : FileName;
}

/// <summary>
/// Line ending types supported by Notepad
/// </summary>
public enum LineEnding
{
    CRLF,   // Windows: \r\n
    LF,     // Unix/Linux/macOS: \n
    CR      // Classic Mac: \r
}

/// <summary>
/// Supported text encodings
/// </summary>
public static class SupportedEncodings
{
    public static readonly EncodingInfo[] All = new[]
    {
        new EncodingInfo("UTF-8", Encoding.UTF8),
        new EncodingInfo("UTF-8 with BOM", new UTF8Encoding(true)),
        new EncodingInfo("UTF-16 LE", Encoding.Unicode),
        new EncodingInfo("UTF-16 BE", Encoding.BigEndianUnicode),
        new EncodingInfo("ANSI", Encoding.Default),
        new EncodingInfo("ASCII", Encoding.ASCII),
    };
    
    public static Encoding DetectEncoding(byte[] bytes)
    {
        // BOM detection
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return new UTF8Encoding(true);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode; // UTF-16 LE
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode; // UTF-16 BE
            
        // Try UTF-8 without BOM
        try
        {
            var utf8 = new UTF8Encoding(false, true);
            utf8.GetString(bytes);
            return Encoding.UTF8;
        }
        catch
        {
            return Encoding.Default; // ANSI fallback
        }
    }
    
    public static LineEnding DetectLineEnding(string content)
    {
        if (content.Contains("\r\n")) return LineEnding.CRLF;
        if (content.Contains("\n")) return LineEnding.LF;
        if (content.Contains("\r")) return LineEnding.CR;
        return LineEnding.CRLF; // Default for Windows
    }
}

public record EncodingInfo(string DisplayName, Encoding Encoding);
