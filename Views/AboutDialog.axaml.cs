using Avalonia.Controls;

namespace NotepadAvalonia.Views;

public partial class AboutDialog : Window
{
    public AboutDialog()
    {
        InitializeComponent();
        var version = typeof(AboutDialog).Assembly.GetName().Version;
        if (version != null) VersionText.Text = $"Version {version.ToString(version.Build < 0 ? 2 : 3)}";
    }

    private void OnClose(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
    }
}
