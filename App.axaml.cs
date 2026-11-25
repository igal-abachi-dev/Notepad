using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using NotepadAvalonia.Models;
using NotepadAvalonia.Services;
using NotepadAvalonia.ViewModels;
using NotepadAvalonia.Views;
using System;
using System.Linq;
using System.IO;
using System.Threading.Tasks;

namespace AvaloniaNotePad;

public partial class App : Application
{
    private bool _checkingShutdown;
    private WindowsSessionEnd? _sessionEnd;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnLastWindowClose;
            desktop.ShutdownRequested += OnShutdownRequested;

            var args = desktop.Args ?? [];
            string? smokeReport = args is ["--verify-release", var report] ? Path.GetFullPath(report) : null;
            string? smokeSettings = smokeReport == null ? null : smokeReport + ".settings.json";
            var draftStore = smokeReport == null ? DraftStore.ForCurrentUser() : new DraftStore(smokeReport + ".drafts");
            var viewModel = new MainWindowViewModel(new FileService(), new SearchService(), new SettingsService(smokeSettings),
                new PrintService(), draftStore: draftStore);
            viewModel.ApplyTheme();
            var main = new MainWindow { DataContext = viewModel };
            desktop.MainWindow = main;
            if (OperatingSystem.IsWindows())
                _sessionEnd = WindowsSessionEnd.TryInstall(() => QueryWindowsSessionEnd(desktop), () => EndWindowsSession(desktop));
            if (smokeReport != null)
                main.Opened += (_, _) => Dispatcher.UIThread.Post(() => _ = ReleaseSmokeTest.RunAsync(desktop, main, smokeReport), DispatcherPriority.Loaded);
            else
            {
                var startup = ParseStartupArguments(args);
                if (startup.DraftId is { } draft)
                    main.Opened += async (_, _) => await viewModel.OpenStartupDraftAsync(draft);
                else if (startup.Path is { } path)
                    main.Opened += async (_, _) => await viewModel.OpenStartupFileAsync(path, startup.Encoding, startup.Print);
            }
        }
        base.OnFrameworkInitializationCompleted();
    }

    internal static (string? Path, FileEncodingType? Encoding, bool Print, Guid? DraftId) ParseStartupArguments(string[] arguments)
    {
        if (arguments.Length == 2 && arguments[0] == "--recover-draft" && Guid.TryParseExact(arguments[1], "N", out var draft))
            return (null, null, false, draft);

        string? path = null;
        FileEncodingType? encoding = null;
        bool print = false;
        bool options = true;
        foreach (string arg in arguments)
        {
            if (!options)
            {
                if (!string.IsNullOrWhiteSpace(arg)) path ??= arg;
                continue;
            }
            switch (arg.ToUpperInvariant())
            {
                case "--": options = false; break;
                case "/A": encoding = FileEncodingType.ANSI; break;
                case "/W": encoding = FileEncodingType.UTF16LE; break;
                case "/P": print = true; break;
                default:
                    if (!string.IsNullOrWhiteSpace(arg)) path ??= arg;
                    break;
            }
        }
        return (path, encoding, print, null);
    }

    private bool QueryWindowsSessionEnd(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var windows = desktop.Windows.OfType<MainWindow>().ToArray();
        if (CanCheckpoint(windows) && CheckpointBeforeSignOut(windows)) return true;
        Dispatcher.UIThread.Post(() => _ = AskToCloseWindowsAsync(windows));
        return false;
    }

    private static void EndWindowsSession(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var windows = desktop.Windows.OfType<MainWindow>().ToArray();
        if (CanCheckpoint(windows)) CheckpointBeforeSignOut(windows);
        foreach (var window in windows)
            window.ApproveSystemClose();
        desktop.Shutdown();
    }

    private async void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;
        var windows = desktop.Windows.OfType<MainWindow>().ToArray();
        if (!windows.Any(window => window.ViewModel.Document.IsModified ||
            window.ViewModel.HasPendingSave || window.ViewModel.HasDraftCleanup)) return;

        if (_sessionEnd == null && !_checkingShutdown && OperatingSystem.IsWindows() &&
            CanCheckpoint(windows) && CheckpointBeforeSignOut(windows))
        {
            foreach (var window in windows)
                window.ApproveSystemClose();
            return;
        }

        e.Cancel = true;
        await AskToCloseWindowsAsync(windows);
    }

    private async Task AskToCloseWindowsAsync(MainWindow[] windows)
    {
        if (_checkingShutdown) return;
        _checkingShutdown = true;
        try
        {
            foreach (var window in windows)
                if (!await window.ConfirmCloseAsync()) return;
            foreach (var window in windows)
                window.CloseApproved();
        }
        finally { _checkingShutdown = false; }
    }

    private static bool CanCheckpoint(MainWindow[] windows) =>
        windows.All(window => window.ViewModel.CanCheckpointForSystemShutdown);

    private static bool CheckpointBeforeSignOut(MainWindow[] windows)
    {
        try
        {
            // The platform needs a synchronous answer. Only the immutable snapshots cross to workers.
            return Task.WhenAll(windows.Select(window => window.ViewModel.CheckpointForSystemShutdownAsync()))
                .Wait(TimeSpan.FromSeconds(2));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or AggregateException)
        {
            return false;
        }
    }

    public MainWindowViewModel? ViewModel =>
        (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow?.DataContext as MainWindowViewModel;

}
