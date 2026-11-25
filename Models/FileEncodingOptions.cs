namespace NotepadAvalonia.Models;

internal static class FileEncodingOptions
{
    internal static readonly (FileEncodingType Type, string Name)[] Choices =
    [
        (FileEncodingType.ANSI, "ANSI"),
        (FileEncodingType.UTF16LE, "UTF-16 LE"),
        (FileEncodingType.UTF16BE, "UTF-16 BE"),
        (FileEncodingType.UTF8BOM, "UTF-8 with BOM"),
        (FileEncodingType.UTF8, "UTF-8")
    ];
}
