using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Notepad.NeoEdit;
using NotepadAvalonia.Models;
using System;
using System.Linq;

namespace NotepadAvalonia.Views;

public partial class FontDialog : Window
{
    public FontDialog() : this(new EditorSettings()) { }

    public FontDialog(EditorSettings settings)
    {
        InitializeComponent();
        var families = FontManager.Current.SystemFonts.Select(font => font.Name)
            .Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase).ToList();
        if (families.Count == 0) families.Add(FontManager.Current.DefaultFontFamily.Name);
        FontComboBox.ItemsSource = families;
        FontComboBox.SelectedItem = families.FirstOrDefault(name => StringComparer.CurrentCultureIgnoreCase.Equals(name, settings.FontFamily))
            ?? families[0];
        SizeBox.Value = (decimal)settings.FontSize;
        BoldBox.IsChecked = settings.IsBold;
        ItalicBox.IsChecked = settings.IsItalic;
        LigaturesBox.IsChecked = settings.EnableFontLigatures;
        UpdatePreview();

        FontComboBox.SelectionChanged += (_, _) => UpdatePreview();
        SizeBox.ValueChanged += (_, _) => UpdatePreview();
        BoldBox.IsCheckedChanged += (_, _) => UpdatePreview();
        ItalicBox.IsCheckedChanged += (_, _) => UpdatePreview();
        LigaturesBox.IsCheckedChanged += (_, _) => UpdatePreview();
    }

    public string SelectedFamily => FontComboBox.SelectedItem as string ?? FontManager.Current.DefaultFontFamily.Name;
    public double SelectedSize => (double)(SizeBox.Value ?? 11);
    public bool IsBold => BoldBox.IsChecked == true;
    public bool IsItalic => ItalicBox.IsChecked == true;
    public bool EnableFontLigatures => LigaturesBox.IsChecked == true;

    private void UpdatePreview()
    {
        PreviewText.FontFamily = new FontFamily(SelectedFamily);
        PreviewText.FontSize = SelectedSize * 96 / 72;
        PreviewText.FontWeight = IsBold ? FontWeight.Bold : FontWeight.Normal;
        PreviewText.FontStyle = IsItalic ? FontStyle.Italic : FontStyle.Normal;
        PreviewText.FontFeatures = EditorFontFeatures.ForLigatures(EnableFontLigatures);
    }

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        if (SizeBox.Value.HasValue) Close(true);
        else SizeBox.Focus();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        FontComboBox.Focus();
    }
}
