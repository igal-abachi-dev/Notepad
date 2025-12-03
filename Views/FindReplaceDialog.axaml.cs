using Avalonia.Controls;
using NotepadAvalonia.ViewModels;

namespace NotepadAvalonia.Views;

public partial class FindReplaceDialog : Window
{
    public FindReplaceDialog()
    {
        InitializeComponent();
    }

    public FindReplaceDialog(MainWindowViewModel vmOwner, bool findMode) : this()
    {

    }
}