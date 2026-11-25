using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
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

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnLastWindowClose;
            desktop.ShutdownRequested += OnShutdownRequested;

            var viewModel = new MainWindowViewModel(new FileService(), new SearchService(), new SettingsService(),
                new PrintService(), draftStore: DraftStore.ForCurrentUser());
            var main = new MainWindow { DataContext = viewModel };
            desktop.MainWindow = main;
            if (desktop.Args != null && ParseStartupArguments(desktop.Args) is { } startup)
                main.Opened += async (_, _) => await viewModel.OpenStartupFileAsync(startup.Path, startup.Encoding, startup.Print);
        }
        base.OnFrameworkInitializationCompleted();
    }

    internal static (string Path, FileEncodingType? Encoding, bool Print)? ParseStartupArguments(string[] arguments)
    {
        FileEncodingType? encoding = null;
        bool print = false;
        foreach (string arg in arguments)
        {
            switch (arg.ToUpperInvariant())
            {
                case "/A": encoding = FileEncodingType.ANSI; break;
                case "/W": encoding = FileEncodingType.UTF16LE; break;
                case "/P": print = true; break;
                default:
                    if (!string.IsNullOrWhiteSpace(arg)) return (arg, encoding, print);
                    break;
            }
        }
        return null;
    }

    private async void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;
        var windows = desktop.Windows.OfType<MainWindow>().ToArray();
        if (!windows.Any(window => window.ViewModel.Document.IsModified ||
            window.ViewModel.HasPendingSave || window.ViewModel.HasDraftCleanup)) return;

        if (!_checkingShutdown && OperatingSystem.IsWindows() &&
            windows.All(window => window.ViewModel.SupportsDraftRecovery) && CheckpointBeforeSignOut(windows))
        {
            foreach (var window in windows)
                window.ApproveSystemClose();
            return;
        }

        e.Cancel = true;
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
