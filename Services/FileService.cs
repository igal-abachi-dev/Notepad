using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using AvaloniaNotePad.Models;

namespace AvaloniaNotePad.Services;

/// <summary>
/// Handles all file I/O operations
/// </summary>
public class FileService
{
    /// <summary>
    /// Opens a file and returns a Document with detected encoding and line ending
    /// </summary>
    public async Task<Document> OpenFileAsync(string filePath)
    {
        var bytes = await File.ReadAllBytesAsync(filePath);
        var encoding = SupportedEncodings.DetectEncoding(bytes);
        var content = encoding.GetString(bytes);
        
        // Remove BOM if present in content
        if (content.Length > 0 && content[0] == '\uFEFF')
            content = content.Substring(1);
            
        var lineEnding = SupportedEncodings.DetectLineEnding(content);
        
        // Normalize line endings internally to \n for the editor
        content = NormalizeLineEndings(content);
        
        return new Document
        {
            FilePath = filePath,
            Content = content,
            Encoding = encoding,
            LineEnding = lineEnding,
            IsModified = false,
            LastModified = File.GetLastWriteTime(filePath)
        };
    }
    
    /// <summary>
    /// Saves a document to file
    /// </summary>
    public async Task SaveFileAsync(Document document, string? filePath = null)
    {
        var path = filePath ?? document.FilePath;
        if (string.IsNullOrEmpty(path))
            throw new ArgumentException("No file path specified");
            
        var content = ConvertLineEndings(document.Content, document.LineEnding);
        
        // Get encoding with or without BOM
        var encoding = document.Encoding;
        var preamble = encoding.GetPreamble();
        
        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        
        // Write BOM if needed (UTF-8 with BOM, UTF-16)
        if (preamble.Length > 0)
            await stream.WriteAsync(preamble, 0, preamble.Length);
            
        var bytes = encoding.GetBytes(content);
        await stream.WriteAsync(bytes, 0, bytes.Length);
        
        document.FilePath = path;
        document.IsModified = false;
        document.LastModified = DateTime.Now;
    }
    
    /// <summary>
    /// Normalize all line endings to \n for internal use
    /// </summary>
    private string NormalizeLineEndings(string content)
    {
        return content
            .Replace("\r\n", "\n")
            .Replace("\r", "\n");
    }
    
    /// <summary>
    /// Convert line endings based on document setting for saving
    /// </summary>
    private string ConvertLineEndings(string content, LineEnding lineEnding)
    {
        var normalized = NormalizeLineEndings(content);
        return lineEnding switch
        {
            LineEnding.CRLF => normalized.Replace("\n", "\r\n"),
            LineEnding.CR => normalized.Replace("\n", "\r"),
            LineEnding.LF => normalized,
            _ => normalized.Replace("\n", "\r\n")
        };
    }
    
    /// <summary>
    /// Check if file exists and is accessible
    /// </summary>
    public bool FileExists(string filePath)
    {
        return File.Exists(filePath);
    }
    
    /// <summary>
    /// Check if file has been modified externally
    /// </summary>
    public bool HasExternalChanges(Document document)
    {
        if (string.IsNullOrEmpty(document.FilePath) || !File.Exists(document.FilePath))
            return false;
            
        var currentModified = File.GetLastWriteTime(document.FilePath);
        return document.LastModified.HasValue && currentModified > document.LastModified.Value;
    }
}
