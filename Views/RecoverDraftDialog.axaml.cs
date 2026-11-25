using Avalonia.Controls;
using Avalonia.Interactivity;
using NotepadAvalonia.Services;
using System;
using System.Collections.Generic;

namespace NotepadAvalonia.Views;

public partial class RecoverDraftDialog : Window
{
    public RecoverDraftDialog() : this(Array.Empty<DraftInfo>()) { }

    public RecoverDraftDialog(IReadOnlyList<DraftInfo> drafts)
    {
        InitializeComponent();
        DraftList.ItemsSource = drafts;
        DraftList.IsVisible = drafts.Count > 0;
        EmptyMessage.IsVisible = drafts.Count == 0;
        DraftList.SelectionChanged += (_, _) => RestoreButton.IsEnabled = DraftList.SelectedItem is DraftInfo;
        DraftList.SelectedIndex = drafts.Count > 0 ? 0 : -1;
        RestoreButton.IsEnabled = drafts.Count > 0;
        DraftList.DoubleTapped += (_, _) => Recover();
        Opened += (_, _) => { if (DraftList.IsVisible) DraftList.Focus(); };
    }

    private void OnRecover(object? sender, RoutedEventArgs e) => Recover();
    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);

    private void Recover()
    {
        if (DraftList.SelectedItem is DraftInfo selection)
            Close(selection);
    }
}
