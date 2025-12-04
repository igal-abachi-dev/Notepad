using System;
using Avalonia.Controls;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

namespace NotepadAvalonia.Views;

public partial class GoToLineDialog : Window
{
    public GoToLineDialog()
    {
        InitializeComponent();
    }

    public GoToLineDialog(int maxLine) : this()
    {
        MaxLine = maxLine;
        DataContext = this;
    }

    public int MaxLine { get; set; }

    private async void OnGoTo(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (int.TryParse(this.FindControl<TextBox>("LineNumberTextBox")?.Text, out var line) &&
            line >= 1 && line <= MaxLine)
        {
            Close(line);
            return;
        }

        await MessageBoxManager.GetMessageBoxStandard(
            "Notepad",
            $"Please enter a line number between 1 and {MaxLine}.",
            ButtonEnum.Ok).ShowAsync();
    }

    private void OnCancel(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(null);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        this.FindControl<TextBox>("LineNumberTextBox")?.Focus();
    }
}
