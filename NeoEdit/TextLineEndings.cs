namespace Notepad.NeoEdit;

internal static class TextLineEndings
{
    public static string Normalize(string text, string ending)
    {
        if (ending == "\r") return text.Replace("\r\n", "\r").Replace('\n', '\r');
        var lf = text.Replace("\r\n", "\n").Replace('\r', '\n');
        return ending == "\r\n" ? lf.Replace("\n", "\r\n") : lf;
    }
}
