using Avalonia.Controls;
using NotepadAvalonia.Models;

namespace NotepadAvalonia.Views;

public partial class PageSetupDialog : Window
{
    public PageSetupDialog()
    {
        InitializeComponent();
        DataContext = this;
    }

    public PageSetupDialog(PageSetupSettings settings) : this()
    {
        Header = settings.Header;
        Footer = settings.Footer;
        MarginTop = settings.MarginTop;
        MarginBottom = settings.MarginBottom;
        MarginLeft = settings.MarginLeft;
        MarginRight = settings.MarginRight;
    }

    public string Header { get; set; } = "&f";
    public string Footer { get; set; } = "Page &p";
    public double MarginTop { get; set; } = 25;
    public double MarginBottom { get; set; } = 25;
    public double MarginLeft { get; set; } = 20;
    public double MarginRight { get; set; } = 20;

    private void OnOk(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var result = new PageSetupSettings
        {
            Header = Header,
            Footer = Footer,
            MarginTop = MarginTop,
            MarginBottom = MarginBottom,
            MarginLeft = MarginLeft,
            MarginRight = MarginRight
        };

        Close(result);
    }

    private void OnCancel(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(null);
    }
}
