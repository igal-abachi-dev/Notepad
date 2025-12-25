using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Microsoft.VisualBasic;
using System;
using System.Linq;
using Notepad.NeoEdit;
using NotepadAvalonia.ViewModels;

namespace NotepadAvalonia.Views;

public partial class MainWindow : Window
{
    public MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;
    private NeoEditor? _editor;
    public MainWindow()
    {
        InitializeComponent();

        _editor = this.FindControl<NeoEditor>("Editor");
        DataContextChanged += OnDataContextChanged;

        // Wire up editor events
        if (_editor != null)
        {
            _editor.CaretMoved += OnCaretMoved;

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
            vm.InitializeEditor(_editor);
        }
    }

    private void ApplyWindowPlacement()
    {
        if (DataContext is MainWindowViewModel vm)
        {
            Position = new PixelPoint(vm.Settings.WindowX, vm.Settings.WindowY);
        }
    }
    private void OnCaretMoved(object? sender, (int Line, int Column) e)
    {
        ViewModel?.UpdateCaretPosition(e.Line, e.Column);
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
    private bool _isForceClosing = false;

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (_isForceClosing)
        {
            base.OnClosing(e);
            return;
        }
        if (DataContext is MainWindowViewModel vm)
        {
            // Check for unsaved changes
            if (vm.Document.IsModified)
            {
                // 2. CRITICAL: Cancel the close immediately so we can show a dialog async
                e.Cancel = true;

                // 3. Ask user
                bool canClose = await ViewModel.CheckSaveChangesAsync();

                if (canClose)
                {
                    // 4. Save settings
                    ViewModel.SaveWindowPlacement(Position, ClientSize);
                    ViewModel.SaveSessionSettings();

                    // 5. Set flag and re-trigger close
                    _isForceClosing = true;
                    Close();
                }
                // IMPORTANT: Return here. 
                // If we cancelled (e.Cancel=true), we stop.
                // If we re-triggered Close(), that new call will hit the `if(_isForceClosing)` block.
                return;
            }

                // Not modified, just save settings and close
                ViewModel?.SaveWindowPlacement(Position, ClientSize);
                ViewModel?.SaveSessionSettings();
            
        }
        base.OnClosing(e);
    }
}
