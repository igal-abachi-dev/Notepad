namespace Notepad.NeoEdit;

internal static class TextLineEndings
{
    public static string Normalize(string text, string ending)
    {
        if (ending == "\r") return text.Replace("\r\n", "\r").Replace('\n', '\r');
        var lf = text.Replace("\r\n", "\n").Replace('\r', '\n');
        return ending == "\r\n" ? lf.Replace("\n", "\r\n") : lf;
    }

    public static int ConvertOffset(string text, int offset, string ending)
    {
        int delta = 0;
        for (int index = 0; index < offset; index++)
        {
            if (text[index] is not ('\r' or '\n')) continue;
            int length = text[index] == '\r' && index + 1 < offset && text[index + 1] == '\n' ? 2 : 1;
            delta += ending.Length - length;
            index += length - 1;
        }
        return offset + delta;
    }
}
