using Avalonia.Controls;
using Avalonia.Interactivity;
using NotepadAvalonia.Models;
using NotepadAvalonia.Services;
using System;
using System.ComponentModel;
using System.IO;

namespace NotepadAvalonia.Views;

public partial class PageSetupDialog : Window
{
    private static readonly string[] PaperNames = ["Printer default", "A4", "Letter", "Legal"];
    private PageSetupSettings _deviceSettings;

    public PageSetupDialog() : this(new(), new(), "Untitled", "") { }

    public PageSetupDialog(PageSetupSettings setup, EditorSettings font, string filename, string text)
    {
        InitializeComponent();
        _deviceSettings = setup;
        PaperBox.ItemsSource = PaperNames;
        PrinterButton.IsVisible = OperatingSystem.IsWindows();
        SheetPreview.Initialize(filename, text, font);
        Populate(setup);
        PaperBox.SelectionChanged += (_, _) => UpdatePreview();
        OrientationBox.SelectionChanged += (_, _) => UpdatePreview();
        foreach (var input in new[] { LeftBox, RightBox, TopBox, BottomBox })
            input.ValueChanged += (_, _) => UpdatePreview();
        HeaderBox.TextChanged += (_, _) => UpdatePreview();
        FooterBox.TextChanged += (_, _) => UpdatePreview();
        UpdatePreview();
    }

    private void Populate(PageSetupSettings setup)
    {
        PaperBox.SelectedIndex = Math.Clamp((int)setup.Paper, 0, PaperNames.Length - 1);
        OrientationBox.SelectedIndex = setup.Landscape ? 1 : 0;
        LeftBox.Value = ToMargin(setup.MarginLeft, 20);
        RightBox.Value = ToMargin(setup.MarginRight, 20);
        TopBox.Value = ToMargin(setup.MarginTop, 25);
        BottomBox.Value = ToMargin(setup.MarginBottom, 25);
        HeaderBox.Text = setup.Header;
        FooterBox.Text = setup.Footer;

        static decimal ToMargin(double value, double fallback) =>
            (decimal)(double.IsFinite(value) ? Math.Clamp(value, 0, 500) : fallback);
    }

    private PageSetupSettings GetSettings() => new()
    {
        Header = HeaderBox.Text ?? "",
        Footer = FooterBox.Text ?? "",
        Paper = (PaperSize)Math.Max(0, PaperBox.SelectedIndex),
        Landscape = OrientationBox.SelectedIndex == 1,
        MarginLeft = (double)(LeftBox.Value ?? -1),
        MarginRight = (double)(RightBox.Value ?? -1),
        MarginTop = (double)(TopBox.Value ?? -1),
        MarginBottom = (double)(BottomBox.Value ?? -1),
        PrinterPaperWidth = _deviceSettings.PrinterPaperWidth,
        PrinterPaperHeight = _deviceSettings.PrinterPaperHeight,
        Printer = _deviceSettings.Printer
    };

    private void UpdatePreview()
    {
        Problem.IsVisible = false;
        SheetPreview.ShowSettings(GetSettings());
    }

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        var setup = GetSettings();
        if (!setup.HasValidMargins())
        {
            ShowProblem("Enter margins that leave room for text on the selected paper.");
            var (width, _) = setup.PaperDimensions;
            var input = setup.MarginLeft < 0 ? LeftBox : setup.MarginRight < 0 ? RightBox :
                setup.MarginTop < 0 ? TopBox : setup.MarginBottom < 0 ? BottomBox :
                setup.MarginLeft + setup.MarginRight >= width ? LeftBox : TopBox;
            input.Focus();
            return;
        }
        Close(setup);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);

    private void OnPrinter(object? sender, RoutedEventArgs e)
    {
        if (!OperatingSystem.IsWindows() || TryGetPlatformHandle() is not { } owner) return;
        var setup = GetSettings();
        if (!setup.HasValidMargins())
        {
            ShowProblem("Enter valid margins before configuring the printer.");
            return;
        }
        try
        {
            if (!WindowsPrinting.ConfigurePage(owner.Handle, setup)) return;
            _deviceSettings = setup;
            Populate(setup);
            UpdatePreview();
        }
        catch (Exception ex) when (ex is Win32Exception or IOException)
        {
            ShowProblem(ex.Message);
        }
    }

    private void ShowProblem(string text)
    {
        Problem.Text = text;
        Problem.IsVisible = true;
    }
}
