using Avalonia.Media;

namespace Notepad.NeoEdit;

internal static class EditorFontFeatures
{
    private static readonly FontFeatureCollection LigaturesOn = new()
    {
        FontFeature.Parse("+calt"), FontFeature.Parse("+liga"), FontFeature.Parse("+clig")
    };
    private static readonly FontFeatureCollection LigaturesOff = new()
    {
        FontFeature.Parse("-calt"), FontFeature.Parse("-liga"), FontFeature.Parse("-clig")
    };

    internal static FontFeatureCollection ForLigatures(bool enabled) => enabled ? LigaturesOn : LigaturesOff;
}
