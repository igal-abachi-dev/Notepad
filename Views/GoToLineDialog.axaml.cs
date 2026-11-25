using System;
using Avalonia.Controls;
using System.Globalization;

namespace NotepadAvalonia.Views;

public partial class GoToLineDialog : Window
{
    public GoToLineDialog()
    {
        InitializeComponent();
    }

    public GoToLineDialog(int maxLine, int currentLine = 1) : this()
    {
        MaxLine = maxLine;
        LineNumberTextBox.Text = Math.Clamp(currentLine, 1, maxLine).ToString(CultureInfo.CurrentCulture);
        DataContext = this;
    }

    public int MaxLine { get; set; }

    private void OnGoTo(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (int.TryParse(this.FindControl<TextBox>("LineNumberTextBox")?.Text, out var line) &&
            line >= 1 && line <= MaxLine)
        {
            Close(line);
            return;
        }

        ValidationMessage.Text = $"Enter a line number from 1 to {MaxLine}.";
        ValidationMessage.IsVisible = true;
        LineNumberTextBox.Focus();
        LineNumberTextBox.SelectAll();
    }

    private void OnCancel(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(null);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        LineNumberTextBox.Focus();
        LineNumberTextBox.SelectAll();
    }
}
