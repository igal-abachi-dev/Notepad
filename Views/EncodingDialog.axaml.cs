using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using NotepadAvalonia.Models;

namespace NotepadAvalonia.Views;

public partial class EncodingDialog : Window
{
    public record EncodingOption(FileEncodingType Type, string Name)
    {
        public override string ToString() => Name;
    }

    public List<EncodingOption> Options { get; }

    public EncodingOption? SelectedOption { get; set; }

    public EncodingDialog() : this(FileEncodingType.ANSI) { }

    public EncodingDialog(FileEncodingType initial)
    {
        InitializeComponent();
        Options = new List<EncodingOption>
        {
            new(FileEncodingType.ANSI, "ANSI"),
            new(FileEncodingType.UTF16LE, "Unicode"),
            new(FileEncodingType.UTF16BE, "Unicode big endian"),
            new(FileEncodingType.UTF8BOM, "UTF-8 with BOM"),
            new(FileEncodingType.UTF8, "UTF-8")
        };
        SelectedOption = Options.FirstOrDefault(o => o.Type == initial) ?? Options[0];
        DataContext = this;
    }

    private void OnOk(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(SelectedOption?.Type);
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
