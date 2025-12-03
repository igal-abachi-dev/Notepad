using Avalonia.Controls;
using Avalonia.Input;
using AvaloniaEdit;
using Microsoft.VisualBasic;
using System;
using System.Linq;
using NotepadAvalonia.ViewModels;

namespace NotepadAvalonia.Views;

public partial class MainWindow : Window
{
    public MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;
    public MainWindow()
    {
        InitializeComponent();

        // Wire up editor events
        var editor = this.FindControl<TextEditor>("Editor");
        if (editor != null)
        {
            editor.TextArea.Caret.PositionChanged += OnCaretPositionChanged;

            // Give ViewModel access to editor for commands
            if (DataContext is MainWindowViewModel vm)
            {
                vm.TextEditor = editor;
            }
        }

        // Handle drag-drop (maps to WM_DROPFILES)
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private void OnCaretPositionChanged(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            var editor = this.FindControl<TextEditor>("Editor");
            if (editor != null)
            {
                var line = editor.TextArea.Caret.Line;
                var column = editor.TextArea.Caret.Column;
                vm.UpdateCaretPosition(line, column);
            }
        }
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (e.Data.Contains(DataFormats.FileNames))
        {
            var files = e.Data.GetFileNames()?.ToList();
            if (files?.Count > 0 && DataContext is MainWindowViewModel vm)
            {
                // Load first file (like original Notepad)
                await vm.OpenFileCommand.ExecuteAsync(files[0]);
            }
        }
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            // Check for unsaved changes
            if (vm.Document.IsModified)
            {
                e.Cancel = true;
                var shouldClose = await vm.CheckSaveChangesAsync();
                if (shouldClose)
                {
                    vm.Document.IsModified = false;
                    Close();
                }
            }
        }
        base.OnClosing(e);
    }
}