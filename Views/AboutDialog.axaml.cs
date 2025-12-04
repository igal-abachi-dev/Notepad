using Avalonia.Controls;

namespace NotepadAvalonia.Views;

public partial class AboutDialog : Window
{
    public AboutDialog()
    {
        InitializeComponent();
    }

    private void OnClose(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
    }
}
