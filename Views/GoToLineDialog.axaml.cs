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
        MaxLine = Math.Max(1, maxLine);
        InfoText.Text = $"Line number (1 - {MaxLine})";
        LineNumberTextBox.Text = Math.Clamp(currentLine, 1, MaxLine).ToString(CultureInfo.CurrentCulture);
    }

    public int MaxLine { get; } = 1;

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
