using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using NotepadAvalonia.ViewModels;
using System;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace NotepadAvalonia.Views;

public partial class MainWindow : Window
{
    private MainWindowViewModel? _viewModel;
    private bool _closeDecisionPending;
    private bool _closeApproved;
    private bool _systemCloseApproved;
    private PixelPoint _normalPosition;
    private Size _normalSize;

    public MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;

    public MainWindow()
    {
        InitializeComponent();
        if (OperatingSystem.IsWindows() && TryGetPlatformHandle() is { HandleDescriptor: "HWND" })
            Win32Properties.AddWndProcHookCallback(this, OnWin32Message);
        DataContextChanged += OnDataContextChanged;
        PositionChanged += (_, _) => RememberPlacement();
        SizeChanged += (_, _) => RememberPlacement();
        Opened += (_, _) =>
        {
            var screen = Screens.ScreenFromPoint(Position) ?? Screens.Primary;
            if (screen != null)
            {
                var area = screen.WorkingArea;
                Width = Math.Min(Width, Math.Max(MinWidth, area.Width / screen.Scaling));
                Height = Math.Min(Height, Math.Max(MinHeight, area.Height / screen.Scaling));
                Position = new PixelPoint(
                    Math.Clamp(Position.X, area.X, Math.Max(area.X, area.Right - 96)),
                    Math.Clamp(Position.Y, area.Y, Math.Max(area.Y, area.Bottom - 48)));
            }
            RememberPlacement();
            Editor.Focus();
        };
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private nint OnWin32Message(nint window, uint message, nint wParam, nint lParam, ref bool handled) =>
        message == 0x0288 // WM_IME_REQUEST
            ? Editor.HandleWindowsImeRequest(wParam, lParam, ref handled)
            : 0;

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel != null)
        {
            _viewModel.RecentFiles.CollectionChanged -= OnRecentFilesChanged;
            _viewModel.OwnerWindow = null;
            _viewModel.DetachEditor();
        }
        _viewModel = DataContext as MainWindowViewModel;
        if (_viewModel != null)
        {
            _viewModel.OwnerWindow = this;
            _viewModel.InitializeEditor(Editor);
            _viewModel.RecentFiles.CollectionChanged += OnRecentFilesChanged;
            Width = _viewModel.Settings.WindowWidth;
            Height = _viewModel.Settings.WindowHeight;
            Position = new PixelPoint(_viewModel.Settings.WindowX, _viewModel.Settings.WindowY);
            _normalSize = new Size(Width, Height);
            _normalPosition = Position;
        }
        OnRecentFilesChanged(this, null);
    }

    private void RememberPlacement()
    {
        if (WindowState != WindowState.Normal || !IsVisible || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        _normalPosition = Position;
        _normalSize = Bounds.Size;
    }

    private void OnRecentFilesChanged(object? sender, NotifyCollectionChangedEventArgs? e)
    {
        RecentMenu.Items.Clear();
        if (_viewModel == null || _viewModel.RecentFiles.Count == 0)
        {
            RecentMenu.IsEnabled = false;
            return;
        }

        RecentMenu.IsEnabled = true;
        foreach (string path in _viewModel.RecentFiles)
        {
            var item = new MenuItem
            {
                Header = path.Replace("_", "__", StringComparison.Ordinal),
                Command = _viewModel.OpenRecentFileCommand,
                CommandParameter = path
            };
            RecentMenu.Items.Add(item);
        }
    }

    private static string? FirstFile(DragEventArgs e) =>
        e.DataTransfer.TryGetFiles()?.Select(item => item.TryGetLocalPath()).FirstOrDefault(path => File.Exists(path));

    private static void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = FirstFile(e) == null ? DragDropEffects.None : DragDropEffects.Copy;
        e.Handled = true;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (FirstFile(e) is not { } path || _viewModel == null) return;
        e.Handled = true;
        await _viewModel.OpenFileAsync(path);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        bool systemCloseApproved = _systemCloseApproved && e.CloseReason == WindowCloseReason.ApplicationShutdown;
        _systemCloseApproved = false;
        if (e.Cancel || _viewModel == null) return;
        if (!_closeApproved && !systemCloseApproved && (_closeDecisionPending || _viewModel.Document.IsModified ||
            _viewModel.HasPendingSave || _viewModel.HasDraftCleanup))
        {
            e.Cancel = true;
            if (!_closeDecisionPending) _ = RequestCloseAsync();
            return;
        }
        RememberPlacement();
        if (_normalSize.Width > 0 && _normalSize.Height > 0)
            _viewModel.SaveWindowPlacement(_normalPosition, _normalSize);
        _viewModel.SaveSessionSettings();
    }

    internal async Task<bool> ConfirmCloseAsync()
    {
        if (_closeDecisionPending) return false;
        _closeDecisionPending = true;
        try { return _viewModel == null || await _viewModel.ConfirmWindowCloseAsync(); }
        finally { _closeDecisionPending = false; }
    }

    internal void CloseApproved()
    {
        _closeApproved = true;
        Close();
        _closeApproved = false;
    }

    internal void ApproveSystemClose() => _systemCloseApproved = true;

    private async Task RequestCloseAsync()
    {
        if (await ConfirmCloseAsync()) CloseApproved();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_viewModel != null)
        {
            _viewModel.RecentFiles.CollectionChanged -= OnRecentFilesChanged;
            _viewModel.ReleaseAfterWindowClosed();
            _viewModel.OwnerWindow = null;
        }
        base.OnClosed(e);
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
            ReferenceEquals(desktop.MainWindow, this))
            desktop.MainWindow = desktop.Windows.OfType<MainWindow>().FirstOrDefault(window => window != this && window.IsVisible);
    }
}
