using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using AvaloniaNotePad.Models;
using AvaloniaNotePad.ViewModels;

namespace AvaloniaNotePad.Views;

public partial class MainWindow : Window
{
    public MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel();
        
        // Set up commands
        SetupCommands();
        
        // Handle window closing
        Closing += OnWindowClosing;
    }

    private void SetupCommands()
    {
        var vm = ViewModel;
        
        // File commands
        vm.NewDocumentCommand = new RelayCommand(() => vm.NewDocument());
        vm.NewWindowCommand = new RelayCommand(OpenNewWindow);
        vm.OpenCommand = new RelayCommand(async () => await OpenFileDialogAsync());
        vm.SaveCommand = new RelayCommand(async () => await SaveAsync());
        vm.SaveAsCommand = new RelayCommand(async () => await SaveAsDialogAsync());
        vm.CloseTabCommand = new RelayCommand<object>(CloseTab);
        vm.ExitCommand = new RelayCommand(Close);
        
        // Edit commands
        vm.UndoCommand = new RelayCommand(Undo);
        vm.RedoCommand = new RelayCommand(Redo);
        vm.CutCommand = new RelayCommand(Cut);
        vm.CopyCommand = new RelayCommand(Copy);
        vm.PasteCommand = new RelayCommand(Paste);
        vm.DeleteCommand = new RelayCommand(() => vm.Delete());
        vm.SelectAllCommand = new RelayCommand(() => vm.SelectAll());
        vm.InsertDateTimeCommand = new RelayCommand(() => vm.InsertDateTime());
        
        // Find/Replace commands
        vm.FindCommand = new RelayCommand(() => vm.ShowFind());
        vm.ReplaceCommand = new RelayCommand(() => vm.ShowReplace());
        vm.FindNextCommand = new RelayCommand(() => vm.FindNext());
        vm.FindPreviousCommand = new RelayCommand(() => vm.FindPrevious());
        vm.ReplaceCurrentCommand = new RelayCommand(() => vm.ReplaceCurrent());
        vm.ReplaceAllCommand = new RelayCommand(() => vm.ReplaceAll());
        vm.HideFindReplaceCommand = new RelayCommand(() => vm.HideFindReplace());
        vm.GoToLineCommand = new RelayCommand(async () => await ShowGoToLineDialogAsync());
        
        // View commands
        vm.ZoomInCommand = new RelayCommand(() => vm.ZoomIn());
        vm.ZoomOutCommand = new RelayCommand(() => vm.ZoomOut());
        vm.ResetZoomCommand = new RelayCommand(() => vm.ResetZoom());
        vm.ToggleWordWrapCommand = new RelayCommand(() => vm.WordWrap = !vm.WordWrap);
        vm.ToggleLineNumbersCommand = new RelayCommand(() => vm.ShowLineNumbers = !vm.ShowLineNumbers);
        vm.ToggleStatusBarCommand = new RelayCommand(() => vm.ShowStatusBar = !vm.ShowStatusBar);
        
        // Format commands
        vm.FontCommand = new RelayCommand(async () => await ShowFontDialogAsync());
        vm.SetEncodingCommand = new RelayCommand<string>(SetEncoding);
        vm.SetLineEndingCommand = new RelayCommand<string>(SetLineEnding);
        
        // Help
        vm.AboutCommand = new RelayCommand(async () => await ShowAboutDialogAsync());
        vm.PrintCommand = new RelayCommand(Print);
        vm.PageSetupCommand = new RelayCommand(PageSetup);
    }

    // === File Operations ===
    
    private async Task OpenFileDialogAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open",
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Text Documents") { Patterns = new[] { "*.txt" } },
                new FilePickerFileType("All Files") { Patterns = new[] { "*.*" } }
            }
        });

        foreach (var file in files)
        {
            await ViewModel.OpenFileAsync(file.Path.LocalPath);
        }
    }

    private async Task SaveAsync()
    {
        if (ViewModel.SelectedTab?.Document.IsNewDocument == true)
        {
            await SaveAsDialogAsync();
        }
        else
        {
            await ViewModel.SaveAsync();
        }
    }

    private async Task SaveAsDialogAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save As",
            SuggestedFileName = ViewModel.SelectedTab?.Document.FileName ?? "Untitled.txt",
            DefaultExtension = "txt",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("Text Documents") { Patterns = new[] { "*.txt" } },
                new FilePickerFileType("All Files") { Patterns = new[] { "*.*" } }
            }
        });

        if (file != null)
        {
            await ViewModel.SaveAsAsync(file.Path.LocalPath);
        }
    }

    private void CloseTab(object? parameter)
    {
        var tab = parameter as TabViewModel ?? ViewModel.SelectedTab;
        if (tab == null) return;

        if (tab.IsModified)
        {
            _ = PromptSaveAndCloseAsync(tab);
        }
        else
        {
            ViewModel.CloseTab(tab);
        }
    }

    private async Task PromptSaveAndCloseAsync(TabViewModel tab)
    {
        // Simple dialog - in a real app, use a proper dialog
        var result = await MessageBox.ShowAsync(this,
            $"Do you want to save changes to {tab.Document.FileName}?",
            "Notepad",
            MessageBoxButtons.YesNoCancel);

        switch (result)
        {
            case MessageBoxResult.Yes:
                if (tab.Document.IsNewDocument)
                {
                    var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                    {
                        SuggestedFileName = tab.Document.FileName
                    });
                    if (file != null)
                    {
                        await ViewModel.SaveAsAsync(file.Path.LocalPath);
                        ViewModel.CloseTab(tab);
                    }
                }
                else
                {
                    await ViewModel.SaveAsync();
                    ViewModel.CloseTab(tab);
                }
                break;
            case MessageBoxResult.No:
                tab.IsModified = false;
                ViewModel.CloseTab(tab);
                break;
            // Cancel: do nothing
        }
    }

    private async void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        // Check for unsaved changes
        var unsaved = ViewModel.Tabs.Where(t => t.IsModified).ToList();
        if (unsaved.Any())
        {
            e.Cancel = true;
            foreach (var tab in unsaved)
            {
                await PromptSaveAndCloseAsync(tab);
            }
            if (!ViewModel.Tabs.Any(t => t.IsModified))
            {
                await ViewModel.SaveSettingsAsync();
                Close();
            }
        }
        else
        {
            await ViewModel.SaveSettingsAsync();
        }
    }

    private void OpenNewWindow()
    {
        var window = new MainWindow();
        window.Show();
    }

    // === Edit Operations (forwarded to TextEditor) ===
    
    private void Undo()
    {
        // Get current TextEditor and call Undo
        // This would need to be wired up to the actual editor instance
    }

    private void Redo() { }
    private void Cut() { }
    private void Copy() { }
    private void Paste() { }

    // === Dialogs ===
    
    private async Task ShowGoToLineDialogAsync()
    {
        if (ViewModel.SelectedTab == null) return;

        var dialog = new Window
        {
            Title = "Go To Line",
            Width = 300,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };

        var lineBox = new NumericUpDown
        {
            Minimum = 1,
            Maximum = ViewModel.SelectedTab.LineCount,
            Value = ViewModel.SelectedTab.CurrentLine,
            Margin = new Thickness(10)
        };

        var okButton = new Button { Content = "Go To", Margin = new Thickness(10) };
        var cancelButton = new Button { Content = "Cancel", Margin = new Thickness(10) };

        okButton.Click += (_, _) =>
        {
            if (lineBox.Value.HasValue)
                ViewModel.GoToLine((int)lineBox.Value.Value);
            dialog.Close();
        };
        cancelButton.Click += (_, _) => dialog.Close();

        var panel = new StackPanel
        {
            Children =
            {
                new TextBlock { Text = "Line number:", Margin = new Thickness(10, 10, 10, 0) },
                lineBox,
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Children = { okButton, cancelButton }
                }
            }
        };

        dialog.Content = panel;
        await dialog.ShowDialog(this);
    }

    private async Task ShowFontDialogAsync()
    {
        // Font dialog would go here - Avalonia doesn't have a built-in one
        // You'd create a custom dialog with font family, size, style options
        await Task.CompletedTask;
    }

    private async Task ShowAboutDialogAsync()
    {
        await MessageBox.ShowAsync(this,
            "Avalonia Notepad\n\nA cross-platform text editor inspired by Windows Notepad.\n\nBuilt with Avalonia UI and .NET 8",
            "About Notepad",
            MessageBoxButtons.Ok);
    }

    private void SetEncoding(string? encoding)
    {
        if (encoding == null) return;
        
        var enc = encoding switch
        {
            "UTF-8" => System.Text.Encoding.UTF8,
            "UTF-8-BOM" => new System.Text.UTF8Encoding(true),
            "UTF-16LE" => System.Text.Encoding.Unicode,
            "UTF-16BE" => System.Text.Encoding.BigEndianUnicode,
            "ANSI" => System.Text.Encoding.Default,
            _ => System.Text.Encoding.UTF8
        };
        
        ViewModel.SetEncoding(enc);
    }

    private void SetLineEnding(string? lineEnding)
    {
        if (lineEnding == null) return;
        
        var le = lineEnding switch
        {
            "CRLF" => LineEnding.CRLF,
            "LF" => LineEnding.LF,
            "CR" => LineEnding.CR,
            _ => LineEnding.CRLF
        };
        
        ViewModel.SetLineEnding(le);
    }

    private void Print()
    {
        // Platform-specific print implementation would go here
    }

    private void PageSetup()
    {
        // Page setup dialog would go here
    }

}

// Simple RelayCommand implementation
public class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) => _execute();
    public event EventHandler? CanExecuteChanged;
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public class RelayCommand<T> : ICommand
{
    private readonly Action<T?> _execute;
    private readonly Func<T?, bool>? _canExecute;

    public RelayCommand(Action<T?> execute, Func<T?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke((T?)parameter) ?? true;
    public void Execute(object? parameter) => _execute((T?)parameter);
    public event EventHandler? CanExecuteChanged;
}

// Simple MessageBox helper
public static class MessageBox
{
    public static async Task<MessageBoxResult> ShowAsync(Window owner, string message, string title, MessageBoxButtons buttons)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 400,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };

        var result = MessageBoxResult.Cancel;
        var buttonPanel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Spacing = 8
        };

        if (buttons == MessageBoxButtons.Ok || buttons == MessageBoxButtons.YesNoCancel)
        {
            if (buttons == MessageBoxButtons.YesNoCancel)
            {
                var yesBtn = new Button { Content = "Yes" };
                var noBtn = new Button { Content = "No" };
                var cancelBtn = new Button { Content = "Cancel" };
                
                yesBtn.Click += (_, _) => { result = MessageBoxResult.Yes; dialog.Close(); };
                noBtn.Click += (_, _) => { result = MessageBoxResult.No; dialog.Close(); };
                cancelBtn.Click += (_, _) => { result = MessageBoxResult.Cancel; dialog.Close(); };
                
                buttonPanel.Children.Add(yesBtn);
                buttonPanel.Children.Add(noBtn);
                buttonPanel.Children.Add(cancelBtn);
            }
            else
            {
                var okBtn = new Button { Content = "OK" };
                okBtn.Click += (_, _) => { result = MessageBoxResult.Ok; dialog.Close(); };
                buttonPanel.Children.Add(okBtn);
            }
        }

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                buttonPanel
            }
        };

        await dialog.ShowDialog(owner);
        return result;
    }
}

public enum MessageBoxButtons { Ok, YesNoCancel }
public enum MessageBoxResult { Ok, Yes, No, Cancel }
