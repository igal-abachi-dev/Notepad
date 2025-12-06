namespace NotepadAvalonia.Models;

/// <summary>
/// Maps to: g410, g409, g415-g417
/// </summary>
public class SearchSettings
{
    public string SearchString { get; set; } = "";      // g410
    public string ReplaceString { get; set; } = "";     // g409
    public bool SearchUp { get; set; } = false;         // g415 (fReverse)
    public bool WrapAround { get; set; } = false;       // g416
    public bool MatchCase { get; set; } = false;        // g417
    public bool UseRegex { get; set; } = false;         // Extension
    public bool WholeWord { get; set; } = false;        // Extension
}
