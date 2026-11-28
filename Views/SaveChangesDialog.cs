using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NotepadAvalonia.Views;

internal static class SaveChangesDialog
{
    public static Task<DialogChoice> AskAsync(Window owner, string filename) =>
        MessageDialog.ShowAsync(owner, "Notepad", $"Do you want to save changes to {filename}?",
            [("_Save", DialogChoice.Yes), ("Do_n't Save", DialogChoice.No), ("Cancel", DialogChoice.Cancel)]);
}

internal enum DialogChoice { None, Ok, Yes, No, Cancel, UpdateAllLinks, SaveAs, SaveAsAdministrator }
internal enum MessageButtons { Ok, YesNo, YesNoCancel }

internal static class MessageDialog
{
    public static Task<DialogChoice> ShowAsync(Window owner, string title, string message, MessageButtons buttons) =>
        ShowAsync(owner, title, message, buttons switch
        {
            MessageButtons.YesNo => [("_Yes", DialogChoice.Yes), ("_No", DialogChoice.No)],
            MessageButtons.YesNoCancel => [("_Yes", DialogChoice.Yes), ("_No", DialogChoice.No), ("Cancel", DialogChoice.Cancel)],
            _ => [("OK", DialogChoice.Ok)]
        });

    public static Task<DialogChoice> ShowAsync(Window owner, string title, string message,
        IReadOnlyList<(string Label, DialogChoice Choice)> actions, DialogChoice? defaultChoice = null)
    {
        var dialog = new Window
        {
            Title = title, Width = 440, SizeToContent = SizeToContent.Height, CanResize = false,
            ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var buttons = new StackPanel { Classes = { "dialog-actions" } };
        for (int i = 0; i < actions.Count; i++)
        {
            var (label, result) = actions[i];
            var button = new Button
            {
                Content = label, IsDefault = defaultChoice.HasValue ? result == defaultChoice : i == 0,
                IsCancel = result == DialogChoice.Cancel || actions.Count == 1
            };
            if (button.IsDefault && (!button.IsCancel || actions.Count == 1))
                button.Classes.Add("accent");
            AutomationProperties.SetAutomationId(button, $"MessageDialog{result}");
            button.Click += (_, _) => dialog.Close(result);
            buttons.Children.Add(button);
        }
        var panel = new StackPanel { Margin = new Thickness(16), Spacing = 16, UseLayoutRounding = true };
        var body = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetAutomationId(body, "MessageDialogText");
        panel.Children.Add(new ScrollViewer { MaxHeight = 260, Content = body });
        panel.Children.Add(buttons);
        dialog.Content = panel;
        return dialog.ShowDialog<DialogChoice>(owner);
    }
}
