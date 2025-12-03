namespace NotepadAvalonia.Models;

/// <summary>
/// Maps to: g343, g344, g296-g299
/// </summary>
public class PageSetupSettings
{
    // Header/Footer format strings
    // Placeholders: &f=filename, &p=page, &d=date, &t=time
    public string Header { get; set; } = "&f";
    public string Footer { get; set; } = "Page &p";

    // Margins in mm (original is 1/1000 inch or 1/100 mm)
    public double MarginTop { get; set; } = 25;
    public double MarginBottom { get; set; } = 25;
    public double MarginLeft { get; set; } = 20;
    public double MarginRight { get; set; } = 20;
}