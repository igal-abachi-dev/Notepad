using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using NotepadAvalonia.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NotepadAvalonia.Views;

public partial class FilePickerDialog
{
    private enum Column { Name, Type, Modified }
    private readonly record struct EntrySort(Column Column, bool Descending);

    private EntrySort _sort;
    private long _sortRequest;
    private CancellationTokenSource? _folderSuggestionRequest;
    private string? _completedFolder;

    private static bool IsHidden(string name, FileAttributes attributes) =>
        (attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0 ||
        !OperatingSystem.IsWindows() && name.StartsWith('.');

    private async void OnFolderTextChanged(object? sender, TextChangedEventArgs e)
    {
        CloseFolderSuggestions();
        string? input = FolderBox.Text;
        if (_closed || _navigation != null || !FolderBox.IsFocused || string.IsNullOrEmpty(input) ||
            input == _folder || input == _completedFolder) return;
        _completedFolder = null;

        using var request = new CancellationTokenSource();
        _folderSuggestionRequest = request;
        bool showHidden = HiddenFilesBox.IsChecked == true;
        try
        {
            string path = ResolvePath(input);
            await Task.Delay(125, request.Token);
            var results = await Task.Run(() => ReadFolderSuggestions(path, input is "." or "..", showHidden, request.Token));
            if (_closed || request.IsCancellationRequested || !FolderBox.IsFocused || FolderBox.Text != input) return;
            if (results.Length == 0) return;
            FolderSuggestions.ItemsSource = results;
            FolderSuggestionPanel.IsVisible = true;
            PositionFolderSuggestions();
        }
        catch (Exception ex) when (ex is OperationCanceledException || IsPathError(ex)) { }
        finally
        {
            if (_folderSuggestionRequest == request) _folderSuggestionRequest = null;
        }
    }

    private static string[] ReadFolderSuggestions(string path, bool listChildren, bool showHidden, CancellationToken cancellation)
    {
        listChildren |= Path.EndsInDirectorySeparator(path) || Path.GetDirectoryName(path) == null;
        string parent = listChildren ? path : Path.GetDirectoryName(path)!;
        string prefix = listChildren ? "" : Path.GetFileName(path);
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var options = new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = true };
        var matches = new List<DirectoryInfo>();
        foreach (var item in new DirectoryInfo(parent).EnumerateDirectories("*", options))
        {
            cancellation.ThrowIfCancellationRequested();
            if (!item.Name.StartsWith(prefix, comparison)) continue;
            try
            {
                if (showHidden || !IsHidden(item.Name, item.Attributes)) matches.Add(item);
            }
            catch (Exception ex) when (IsPathError(ex)) { }
        }
        return matches.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Name, StringComparer.Ordinal).Take(12).Select(item => item.FullName).ToArray();
    }

    private void CloseFolderSuggestions()
    {
        _folderSuggestionRequest?.Cancel();
        FolderSuggestionPanel.IsVisible = false;
        FolderSuggestions.ItemsSource = null;
    }

    private void PositionFolderSuggestions()
    {
        if (!FolderSuggestionPanel.IsVisible ||
            FolderBox.TranslatePoint(new Point(0, FolderBox.Bounds.Height + 2), FolderSuggestionLayer) is not { } point) return;
        FolderSuggestionPanel.Width = FolderBox.Bounds.Width;
        Canvas.SetLeft(FolderSuggestionPanel, point.X);
        Canvas.SetTop(FolderSuggestionPanel, point.Y);
    }

    private void OnFolderLostFocus(object? sender, RoutedEventArgs e) => CloseFolderSuggestions();

    private async Task<bool> HandleFolderSuggestionKeyAsync(KeyEventArgs e)
    {
        if (!FolderSuggestionPanel.IsVisible || e.KeyModifiers != KeyModifiers.None) return false;
        if (e.Key is Key.Down or Key.Up)
        {
            int index = FolderSuggestions.SelectedIndex;
            index = index < 0 ? (e.Key == Key.Down ? 0 : FolderSuggestions.ItemCount - 1) :
                Math.Clamp(index + (e.Key == Key.Down ? 1 : -1), 0, FolderSuggestions.ItemCount - 1);
            FolderSuggestions.SelectedIndex = index;
            FolderSuggestions.ScrollIntoView(index);
        }
        else if (e.Key == Key.Escape)
            CloseFolderSuggestions();
        else if (FolderSuggestions.SelectedItem is string path && e.Key is Key.Enter or Key.Tab)
        {
            e.Handled = true;
            if (e.Key == Key.Enter) await NavigateAsync(path);
            else
            {
                CloseFolderSuggestions();
                _completedFolder = Path.EndsInDirectorySeparator(path) ? path : path + Path.DirectorySeparatorChar;
                FolderBox.Text = _completedFolder;
                FolderBox.CaretIndex = _completedFolder.Length;
            }
        }
        else return false;
        e.Handled = true;
        return true;
    }

    private async void OnFolderSuggestionTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is not Avalonia.Visual visual ||
            visual.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext is not string path) return;
        e.Handled = true;
        await NavigateAsync(path);
    }

    private async void OnSortEntries(object? sender, RoutedEventArgs e)
    {
        if (_navigation != null || Entries.ItemsSource is not FilePickerEntry[] entries) return;
        Column column = sender == TypeHeader ? Column.Type : sender == ModifiedHeader ? Column.Modified : Column.Name;
        var sort = new EntrySort(column, _sort.Column == column && !_sort.Descending);
        _sort = sort;
        long request = ++_sortRequest;
        var sorted = await Task.Run(() => SortEntries(entries, sort));
        if (_closed || _navigation != null || _sortRequest != request || Entries.ItemsSource != entries) return;

        var selected = Entries.SelectedItem;
        string? filename = FileNameBox.Text;
        Entries.ItemsSource = sorted;
        Entries.SelectedItem = selected;
        FileNameBox.Text = filename;
        UpdateSortHeaders(sort);
    }

    private static FilePickerEntry[] SortEntries(FilePickerEntry[] entries, EntrySort sort)
    {
        var grouped = entries.OrderByDescending(entry => entry.IsFolder);
        var names = StringComparer.CurrentCultureIgnoreCase;
        var ordered = sort.Column switch
        {
            Column.Modified => sort.Descending ? grouped.ThenByDescending(entry => entry.LastWriteTimeUtc) : grouped.ThenBy(entry => entry.LastWriteTimeUtc),
            Column.Type => sort.Descending ? grouped.ThenByDescending(entry => entry.Kind, names) : grouped.ThenBy(entry => entry.Kind, names),
            _ => sort.Descending ? grouped.ThenByDescending(entry => entry.Name, names) : grouped.ThenBy(entry => entry.Name, names)
        };
        return ordered.ThenBy(entry => entry.Name, names).ThenBy(entry => entry.Name, StringComparer.Ordinal).ToArray();
    }

    private void UpdateSortHeaders(EntrySort sort)
    {
        SetHeader(NameHeader, "Name", Column.Name);
        SetHeader(TypeHeader, "Type", Column.Type);
        SetHeader(ModifiedHeader, "Modified", Column.Modified);

        void SetHeader(Button button, string label, Column column)
        {
            button.Content = sort.Column == column ? label + (sort.Descending ? " ▼" : " ▲") : label;
            AutomationProperties.SetName(button, sort.Column == column
                ? $"{label}, sorted {(sort.Descending ? "descending" : "ascending")}" : $"Sort by {label}");
        }
    }

    private async Task RenameEntryAsync(FilePickerEntry entry)
    {
        if (_navigation != null || _accepting || _folder is not { } parent) return;
        string stem = entry.IsFolder ? entry.Name : Path.GetFileNameWithoutExtension(entry.Name);
        int selectionLength = stem.Length == 0 ? entry.Name.Length : stem.Length;
        string title = entry.IsFolder ? "Rename Folder" : "Rename File";
        if (await AskForNameAsync(title, "New name", "_Rename", entry.Name, selectionLength) is not { } name ||
            _closed || name == entry.Name) return;
        try
        {
            if (name is "." or ".." || !DocumentPath.IsValidName(name) ||
                OperatingSystem.IsWindows() && (name.EndsWith('.') || name.EndsWith(' ')))
                throw new IOException("Enter a valid name within this folder.");
            string destination = Path.Combine(parent, name);
            await Task.Run(() =>
            {
                if (entry.IsFolder) Directory.Move(entry.Path, destination);
                else File.Move(entry.Path, destination);
            });

            if (_closed || _folder != parent) return;
            if (!entry.IsFolder && FileNameBox.Text == entry.Name) FileNameBox.Text = name;
            if (await NavigateAsync(parent, remember: false, wildcard: _wildcard) && Entries.ItemsSource is FilePickerEntry[] files)
                Entries.SelectedItem = files.FirstOrDefault(item => SettingsService.PathComparer.Equals(item.Path, destination));
        }
        catch (Exception ex) when (IsPathError(ex))
        {
            if (!_closed) Report($"Cannot rename {entry.Name}. {ex.Message}");
        }
    }
}
