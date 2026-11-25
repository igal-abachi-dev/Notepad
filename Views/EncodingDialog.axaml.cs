using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using NotepadAvalonia.Models;

namespace NotepadAvalonia.Views;

public partial class EncodingDialog : Window
{
    public record EncodingOption(FileEncodingType? Type, string Name)
    {
        public override string ToString() => Name;
    }

    public List<EncodingOption> Options { get; }

    public EncodingOption? SelectedOption => EncodingCombo.SelectedItem as EncodingOption;

    public EncodingDialog() : this(FileEncodingType.UTF8) { }

    public EncodingDialog(FileEncodingType? initial, bool forOpening = false)
    {
        InitializeComponent();
        PromptText.Text = forOpening ? "Select the file’s encoding." : "Select the encoding to save with.";
        Options = new List<EncodingOption>
        {
            new(FileEncodingType.ANSI, "ANSI"),
            new(FileEncodingType.UTF16LE, "UTF-16 LE"),
            new(FileEncodingType.UTF16BE, "UTF-16 BE"),
            new(FileEncodingType.UTF8BOM, "UTF-8 with BOM"),
            new(FileEncodingType.UTF8, "UTF-8")
        };
        if (forOpening) Options.Insert(0, new(null, "Auto-detect"));
        EncodingCombo.ItemsSource = Options;
        EncodingCombo.SelectedItem = Options.FirstOrDefault(o => o.Type == initial) ?? Options[0];
    }

    internal void SetPrompt(string text) => PromptText.Text = text;

    private void OnOk(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (SelectedOption != null) Close(SelectedOption);
    }

    private void OnCancel(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(null);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        this.FindControl<ComboBox>("EncodingCombo")?.Focus();
    }
}
