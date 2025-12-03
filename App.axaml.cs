using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using AvaloniaNotePad.ViewModels;
using AvaloniaNotePad.Views;
using System.IO;

namespace AvaloniaNotePad;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Avoid duplicate validations from both Avalonia and the CommunityToolkit. 
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            DisableAvaloniaDataAnnotationValidation();
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(),
            };

            // Handle command line arguments (open files)
            if (desktop.Args?.Length > 0)
            {
                var mainWindow = desktop.MainWindow as MainWindow;
                foreach (var arg in desktop.Args)
                {
                    if (System.IO.File.Exists(arg))
                    {
                        _ = mainWindow?.ViewModel.OpenFileAsync(arg);
                    }
                }
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    public MainWindowViewModel? ViewModel =>
        (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow?.DataContext
            as MainWindowViewModel;

    private void DisableAvaloniaDataAnnotationValidation()
    {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        // remove each entry found
        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}
