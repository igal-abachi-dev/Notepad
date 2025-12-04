using System;
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
        DataContext = new FindReplaceViewModel(vmOwner, findMode);
        Title = findMode ? "Find" : "Replace";
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        this.FindControl<TextBox>("FindTextBox")?.Focus();
    }
}
