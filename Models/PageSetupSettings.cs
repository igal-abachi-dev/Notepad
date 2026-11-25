using System.Text.Json.Serialization;

namespace NotepadAvalonia.Models;

/// <summary>
/// Maps to: g343, g344, g296-g299
/// </summary>
public class PageSetupSettings
{
    // Header/Footer format strings
    // &f filename, &p page, &d date, &t time, &l &c &r alignment, && literal ampersand.
    public string Header { get; set; } = "&f";
    public string Footer { get; set; } = "Page &p";

    // Margins in mm (original is 1/1000 inch or 1/100 mm)
    public double MarginTop { get; set; } = 25;
    public double MarginBottom { get; set; } = 25;
    public double MarginLeft { get; set; } = 20;
    public double MarginRight { get; set; } = 20;
    public PaperSize Paper { get; set; } = PaperSize.PrinterDefault;
    public bool Landscape { get; set; }
    public double PrinterPaperWidth { get; set; }
    public double PrinterPaperHeight { get; set; }

    [JsonIgnore]
    internal PrinterPageOptions? Printer { get; set; }

    [JsonIgnore]
    public (double Width, double Height) PaperDimensions
    {
        get
        {
            var (width, height) = Paper switch
            {
                PaperSize.Letter => (215.9, 279.4),
                PaperSize.Legal => (215.9, 355.6),
                PaperSize.PrinterDefault when PrinterPaperWidth > 0 && PrinterPaperHeight > 0 =>
                    (PrinterPaperWidth, PrinterPaperHeight),
                _ => (210.0, 297.0)
            };
            return Landscape ? (height, width) : (width, height);
        }
    }

    public bool HasValidMargins()
    {
        var (width, height) = PaperDimensions;
        return double.IsFinite(MarginLeft) && double.IsFinite(MarginRight) &&
            double.IsFinite(MarginTop) && double.IsFinite(MarginBottom) &&
            MarginLeft >= 0 && MarginRight >= 0 && MarginTop >= 0 && MarginBottom >= 0 &&
            MarginLeft + MarginRight < width && MarginTop + MarginBottom < height;
    }
}

public enum PaperSize { PrinterDefault, A4, Letter, Legal }

internal sealed record PrinterPageOptions(byte[] Mode, byte[] Names);
