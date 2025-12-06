using NotepadAvalonia.Models;
using Xunit;

namespace Notepad.Tests;

public class DocumentModelTests
{
    [Fact]
    public void WindowTitle_ForUntitledModified_AddsAsterisk()
    {
        var model = new DocumentModel
        {
            IsUntitled = true,
            IsModified = true
        };

        Assert.Equal("*Untitled - Notepad", model.WindowTitle);
    }

    [Fact]
    public void WindowTitle_ForNamedFile_ShowsFileName()
    {
        var model = new DocumentModel
        {
            IsUntitled = false,
            IsModified = false,
            FilePath = @"C:\temp\notes.txt"
        };

        Assert.Equal("notes.txt - Notepad", model.WindowTitle);
    }
}
