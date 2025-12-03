using Avalonia.Controls;

namespace NotepadAvalonia.Views;

public partial class GoToLineDialog : Window
{
    public GoToLineDialog()
    {
        InitializeComponent();
    }

    public int MaxLine { get; set; }
}