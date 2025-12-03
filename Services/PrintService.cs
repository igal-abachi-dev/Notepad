using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaNotePad.Models;

namespace AvaloniaNotePad.Services;

/// <summary>
/// Print service - cross-platform printing abstraction
/// Note: Actual printing requires platform-specific implementation
/// This provides the data preparation layer
/// </summary>
public class PrintService
{
    public PrintSettings Settings { get; } = new();
    
    /// <summary>
    /// Prepare document for printing by paginating content
    /// </summary>
    public List<PrintPage> PaginateDocument(Document document, PrintSettings settings)
    {
        var pages = new List<PrintPage>();
        var lines = document.Content.Split('\n');
        
        var currentPage = new PrintPage { PageNumber = 1 };
        var linesPerPage = CalculateLinesPerPage(settings);
        var lineCount = 0;
        
        foreach (var line in lines)
        {
            currentPage.Lines.Add(line);
            lineCount++;
            
            if (lineCount >= linesPerPage)
            {
                pages.Add(currentPage);
                currentPage = new PrintPage { PageNumber = pages.Count + 1 };
                lineCount = 0;
            }
        }
        
        if (currentPage.Lines.Any())
            pages.Add(currentPage);
            
        return pages;
    }
    
    /// <summary>
    /// Format header with placeholders
    /// Supported: &f (filename), &p (page), &d (date), &t (time)
    /// </summary>
    public string FormatHeader(string template, Document document, int pageNumber, int totalPages)
    {
        return template
            .Replace("&f", document.FileName)
            .Replace("&p", pageNumber.ToString())
            .Replace("&P", totalPages.ToString())
            .Replace("&d", DateTime.Now.ToShortDateString())
            .Replace("&t", DateTime.Now.ToShortTimeString());
    }
    
    private int CalculateLinesPerPage(PrintSettings settings)
    {
        // Rough estimate based on page size and margins
        var printableHeight = settings.PageHeight - settings.MarginTop - settings.MarginBottom;
        var lineHeight = settings.FontSize * 1.2; // Approximate line height
        return (int)(printableHeight / lineHeight);
    }
}

public class PrintSettings
{
    // Page setup (in 1/100th inch, like Windows)
    public double PageWidth { get; set; } = 850;   // 8.5"
    public double PageHeight { get; set; } = 1100; // 11"
    public double MarginLeft { get; set; } = 75;   // 0.75"
    public double MarginRight { get; set; } = 75;
    public double MarginTop { get; set; } = 100;   // 1"
    public double MarginBottom { get; set; } = 100;
    
    // Header/Footer (Windows Notepad format)
    public string Header { get; set; } = "&f";
    public string Footer { get; set; } = "Page &p";
    
    // Font
    public string FontFamily { get; set; } = "Consolas";
    public double FontSize { get; set; } = 10;
}

public class PrintPage
{
    public int PageNumber { get; set; }
    public List<string> Lines { get; } = new();
}
