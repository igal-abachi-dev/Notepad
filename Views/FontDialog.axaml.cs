using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Media;

namespace NotepadAvalonia.Views;

public partial class FontDialog : Window
{
    public FontDialog()
    {
        InitializeComponent();
        FontFamilies = new List<string>
        {
            "Consolas",
            "Cascadia Code",
            "Segoe UI",
            "Courier New",
            "Arial",
            "Calibri",
            "Times New Roman"
        };
        if (!FontFamilies.Contains(FontFamily) && FontFamilies.Count > 0)
        {
            FontFamily = FontFamilies[0];
        }
        DataContext = this;
    }

    public List<string> FontFamilies { get; }

    public new string FontFamily { get; set; } = "Consolas";

    public new double FontSize { get; set; } = 11;

    public bool IsBold { get; set; }

    public bool IsItalic { get; set; }

    private void OnOk(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(true);
    }

    private void OnCancel(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(false);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        this.FindControl<ComboBox>("FontComboBox")?.Focus();
    }
}
