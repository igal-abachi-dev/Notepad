using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MsBox.Avalonia.Enums;
using System.Threading.Tasks;

namespace NotepadAvalonia.Views;

internal static class SaveChangesDialog
{
    public static Task<ButtonResult> AskAsync(Window owner, string filename)
    {
        var dialog = new Window
        {
            Title = "Notepad",
            Width = 440,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        AddButton("_Save", ButtonResult.Yes, isDefault: true);
        AddButton("Do_n't Save", ButtonResult.No);
        AddButton("Cancel", ButtonResult.Cancel, isCancel: true);
        var panel = new StackPanel { Margin = new Thickness(18), Spacing = 16 };
        panel.Children.Add(new TextBlock { Text = $"Do you want to save changes to {filename}?", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(buttons);
        dialog.Content = panel;
        return dialog.ShowDialog<ButtonResult>(owner);

        void AddButton(string label, ButtonResult result, bool isDefault = false, bool isCancel = false)
        {
            var button = new Button { Content = label, MinWidth = 86, IsDefault = isDefault, IsCancel = isCancel };
            button.Click += (_, _) => dialog.Close(result);
            buttons.Children.Add(button);
        }
    }
}
