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
        Options = CreateOptions(forOpening);
        EncodingCombo.ItemsSource = Options;
        EncodingCombo.SelectedItem = Options.FirstOrDefault(o => o.Type == initial) ?? Options[0];
    }

    internal static List<EncodingOption> CreateOptions(bool forOpening)
    {
        var options = FileEncodingOptions.Choices.Select(choice => new EncodingOption(choice.Type, choice.Name)).ToList();
        if (forOpening) options.Insert(0, new(null, "Auto-detect"));
        return options;
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
