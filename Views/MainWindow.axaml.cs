using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using NotepadAvalonia.ViewModels;
using System;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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
    private NativeMenu? _macRecentFiles;

    public MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;

    public MainWindow()
    {
        InitializeComponent();
        if (OperatingSystem.IsMacOS())
        {
            foreach (var binding in KeyBindings.ToArray())
            {
                if (binding.Gesture is not { } gesture) continue;
                if (gesture.Key == Key.Back && gesture.KeyModifiers == KeyModifiers.Alt)
                    KeyBindings.Remove(binding);
                else
                    binding.Gesture = ToMacGesture(gesture);
            }
            NativeMenu.SetMenu(this, CreateMacMenu(EditorMenu.Items));
            PropertyChanged += (_, e) =>
            {
                if (e.Property == NativeMenu.IsNativeMenuExportedProperty)
                    EditorMenu.IsVisible = !NativeMenu.GetIsNativeMenuExported(this);
            };
        }
        if (OperatingSystem.IsWindows() && TryGetPlatformHandle() is { HandleDescriptor: "HWND" })
        {
            Win32Properties.AddWndProcHookCallback(this, OnWin32Message);
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) && PlatformSettings is { } settings)
                ConfigureMica(settings);
        }
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
            if (OperatingSystem.IsWindows() && TryGetPlatformHandle()?.HandleDescriptor == "HWND")
            {
                UpdateInputDirection(GetKeyboardLayout(0));
                Editor.AttachWindowsTextAutomation(this, EditorSurface);
            }
            if (OperatingSystem.IsMacOS())
                EditorMenu.IsVisible = !NativeMenu.GetIsNativeMenuExported(this);
            Editor.Focus();
        };
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private void ConfigureMica(IPlatformSettings settings)
    {
        var solid = new[] { WindowTransparencyLevel.None };
        var mica = new[] { WindowTransparencyLevel.Mica, WindowTransparencyLevel.None };

        void UpdateChrome() => Classes.Set("mica-chrome",
            ActualTransparencyLevel == WindowTransparencyLevel.Mica &&
            settings.GetColorValues().ContrastPreference != ColorContrastPreference.High);

        void OnColorValuesChanged(object? sender, PlatformColorValues colors)
        {
            TransparencyLevelHint = colors.ContrastPreference == ColorContrastPreference.High ? solid : mica;
            UpdateChrome();
        }

        PropertyChanged += (_, e) =>
        {
            if (e.Property == ActualTransparencyLevelProperty) UpdateChrome();
        };
        settings.ColorValuesChanged += OnColorValuesChanged;
        Closed += (_, _) => settings.ColorValuesChanged -= OnColorValuesChanged;
        OnColorValuesChanged(settings, settings.GetColorValues());
    }

    internal NativeMenu CreateMacMenu(ItemCollection items)
    {
        var menu = new NativeMenu();
        foreach (var entry in items)
        {
            if (entry is Separator)
            {
                menu.Items.Add(new NativeMenuItemSeparator());
                continue;
            }
            if (entry is not MenuItem item || item.Header is not string header) continue;
            if (item.InputGesture is { } gesture) item.InputGesture = ToMacGesture(gesture);
            var nativeItem = new NativeMenuItem(header.Replace("_", "", StringComparison.Ordinal))
            {
                Gesture = item.InputGesture is { } shortcut && shortcut.KeyModifiers.HasFlag(KeyModifiers.Meta) ? shortcut : null,
                ToggleType = (NativeMenuItemToggleType)item.ToggleType
            };
            nativeItem.Bind(NativeMenuItem.CommandProperty, item.GetObservable(MenuItem.CommandProperty));
            nativeItem.Bind(NativeMenuItem.CommandParameterProperty, item.GetObservable(MenuItem.CommandParameterProperty));
            nativeItem.Bind(NativeMenuItem.IsCheckedProperty, item.GetObservable(MenuItem.IsCheckedProperty));
            nativeItem.Bind(NativeMenuItem.IsEnabledProperty, item.GetObservable(MenuItem.IsEnabledProperty));
            nativeItem.Bind(NativeMenuItem.IsVisibleProperty, item.GetObservable(MenuItem.IsVisibleProperty));
            if (item == RecentMenu)
                nativeItem.Menu = _macRecentFiles = new NativeMenu();
            else if (item.Items.Count != 0)
                nativeItem.Menu = CreateMacMenu(item.Items);
            menu.Items.Add(nativeItem);
        }
        return menu;
    }

    internal static KeyGesture ToMacGesture(KeyGesture gesture)
    {
        if ((gesture.KeyModifiers & KeyModifiers.Control) == 0) return gesture;
        if (gesture.Key == Key.Y && gesture.KeyModifiers == KeyModifiers.Control)
            return new(Key.Z, KeyModifiers.Meta | KeyModifiers.Shift);
        if (gesture.Key == Key.H && gesture.KeyModifiers == KeyModifiers.Control)
            return new(Key.F, KeyModifiers.Meta | KeyModifiers.Alt);
        return new(gesture.Key, (gesture.KeyModifiers & ~KeyModifiers.Control) | KeyModifiers.Meta);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new MainWindowAutomationPeer(this);

    private sealed class MainWindowAutomationPeer(MainWindow window) : WindowAutomationPeer(window), IRootProvider
    {
        AutomationPeer? IRootProvider.GetFocus() => window.Editor.IsFocused
            ? window.Editor.GetHostedTextPeer() ?? GetFocus()
            : GetFocus();

        AutomationPeer? IRootProvider.GetPeerFromPoint(Point point)
        {
            if (window.TranslatePoint(point, window.Editor) is { } inEditor &&
                new Rect(window.Editor.Bounds.Size).Contains(inEditor) &&
                window.Editor.GetHostedTextPeer() is { } textPeer)
                return textPeer;
            return GetPeerFromPoint(point);
        }
    }

    private nint OnWin32Message(nint window, uint message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x0288) // WM_IME_REQUEST
            return Editor.HandleWindowsImeRequest(wParam, lParam, ref handled);
        if (message == 0x0051) UpdateInputDirection(lParam); // WM_INPUTLANGCHANGE
        return 0;
    }

    private void UpdateInputDirection(nint layout)
    {
        try { Editor.PreferRightToLeftForNeutralText = CultureInfo.GetCultureInfo((int)(layout & 0xffff)).TextInfo.IsRightToLeft; }
        catch (CultureNotFoundException) { Editor.PreferRightToLeftForNeutralText = false; }
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint GetKeyboardLayout(uint threadId);

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel != null)
        {
            _viewModel.RecentFiles.CollectionChanged -= OnRecentFilesChanged;
            _viewModel.OwnerWindow = null;
            _viewModel.DetachEditor();
        }
        _viewModel = DataContext as MainWindowViewModel;
        if (Editor.ContextMenu is { } menu) menu.DataContext = _viewModel;
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
        _macRecentFiles?.Items.Clear();
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
            _macRecentFiles?.Items.Add(new NativeMenuItem(path)
            {
                Command = item.Command,
                CommandParameter = path
            });
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
