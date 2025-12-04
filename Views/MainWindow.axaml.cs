using Avalonia;
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
    private TextEditor? _editor;
    public MainWindow()
    {
        InitializeComponent();

        _editor = this.FindControl<TextEditor>("Editor");
        DataContextChanged += OnDataContextChanged;

        // Wire up editor events
        if (_editor != null)
        {
            _editor.TextArea.Caret.PositionChanged += OnCaretPositionChanged;

            // Give ViewModel access to editor for commands
            TryAttachEditorToViewModel();
            Opened += (_, _) => _editor?.Focus();
        }

        // Handle drag-drop (maps to WM_DROPFILES)
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        TryAttachEditorToViewModel();
        ApplyWindowPlacement();
    }

    private void TryAttachEditorToViewModel()
    {
        if (_editor == null) return;

        if (DataContext is MainWindowViewModel vm)
        {
            vm.TextEditor = _editor;
        }
    }

    private void ApplyWindowPlacement()
    {
        if (DataContext is MainWindowViewModel vm)
        {
            Position = new PixelPoint(vm.Settings.WindowX, vm.Settings.WindowY);
        }
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
                await vm.OpenFileAsync(files[0]);
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
                var shouldClose = await vm.CheckSaveChangesAsync();
                if (!shouldClose)
                {
                    e.Cancel = true;
                    return;
                }
            }

            vm.SaveWindowPlacement(Position, ClientSize);
            vm.SaveSessionSettings();
        }
        base.OnClosing(e);
    }
}
