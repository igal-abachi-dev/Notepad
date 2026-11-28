using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using NotepadAvalonia.Models;
using NotepadAvalonia.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Enumeration;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NotepadAvalonia.Views;

public partial class FilePickerDialog : Window
{
    internal sealed record Choice(string Path, FileEncodingType? Encoding, bool OverwriteConfirmed);
    private sealed record Location(string Name, string Path)
    {
        public override string ToString() => Name;
    }

    private readonly bool _saving;
    private readonly string? _initialFolder;
    private readonly string? _suggestedName;
    private readonly Stack<string> _history = new();
    private string? _folder;
    private string? _wildcard;
    private CancellationTokenSource? _navigation;
    private bool _accepting;
    private bool _closed;

    public FilePickerDialog() : this(null, null, null) { }

    internal FilePickerDialog(string? currentFile, string? recentFile, FileEncodingType? saveEncoding, string? suggestedName = null)
    {
        InitializeComponent();
        FolderBox.AddHandler(KeyDownEvent, OnFolderKeyDown, RoutingStrategies.Tunnel);
        FolderBox.SizeChanged += (_, _) => PositionFolderSuggestions();
        _saving = saveEncoding.HasValue;
        _initialFolder = Path.GetDirectoryName(currentFile ?? recentFile);
        Title = _saving ? "Save As" : "Open";
        AcceptButton.Content = _saving ? "_Save" : "_Open";
        _suggestedName = _saving && currentFile == null ? suggestedName : null;
        if (_saving) FileNameBox.Text = currentFile != null ? Path.GetFileName(currentFile) : _suggestedName;
        FilterBox.SelectedIndex = 0;
        var encodings = EncodingDialog.CreateOptions(forOpening: !_saving);
        EncodingBox.ItemsSource = encodings;
        EncodingBox.SelectedItem = encodings.First(item => item.Type == saveEncoding);
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        FileNameBox.Focus();
        FileNameBox.SelectAll();
        if (_suggestedName != null) FileNameBox.SelectionEnd = Path.GetFileNameWithoutExtension(_suggestedName).Length;
        // Directory and drive probes can wait on a network location.
        Location[] locations;
        try { locations = await Task.Run(GetLocations); }
        catch (Exception ex) when (IsPathError(ex)) { locations = []; }
        if (_closed) return;
        Places.ItemsSource = locations;
        if (_folder != null || _navigation != null) return;
        if (_initialFolder != null && await NavigateAsync(_initialFolder, remember: false)) return;
        if (!_closed && _navigation == null)
        {
            string location = locations.FirstOrDefault(place => place.Name == "Documents")?.Path ?? Environment.CurrentDirectory;
            await NavigateAsync(location, remember: false);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        CloseFolderSuggestions();
        _navigation?.Cancel();
        base.OnClosed(e);
    }

    private static Location[] GetLocations()
    {
        var locations = new List<Location>();
        void Add(string label, string folder)
        {
            if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder)) locations.Add(new(label, folder));
        }

        Add("Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        Add("Desktop", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Add("Downloads", Path.Combine(home, "Downloads"));
        Add("Home", home);
        if (OperatingSystem.IsWindows())
        {
            foreach (string drive in Environment.GetLogicalDrives()) locations.Add(new(drive, drive));
        }
        else
            locations.Add(new("Filesystem", "/"));
        return locations.ToArray();
    }

    private async Task<bool> NavigateAsync(string path, bool remember = true, string? wildcard = null)
    {
        CloseFolderSuggestions();
        _navigation?.Cancel();
        using var request = new CancellationTokenSource();
        _navigation = request;
        string pattern = wildcard ?? (FilterBox.SelectedIndex == 1 ? "*" : "*.txt");
        bool showHidden = HiddenFilesBox.IsChecked == true;
        var sort = _sort;
        Report("Loading folder…");
        AcceptButton.IsEnabled = false;
        try
        {
            var files = await Task.Run(() => SortEntries(ReadEntries(path, pattern, showHidden, request.Token), sort));
            if (_closed || request.IsCancellationRequested) return false;
            if (remember && _folder != null && !SettingsService.PathComparer.Equals(_folder, path))
                _history.Push(_folder);
            _folder = path;
            _wildcard = wildcard;
            FolderBox.Text = path;
            Entries.ItemsSource = files;
            UpdateSortHeaders(sort);
            Report(files.Length == 0 ? "No matching files or folders." : "");
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex) when (IsPathError(ex))
        {
            if (!_closed && !request.IsCancellationRequested) Report($"Cannot open this folder. {ex.Message}");
            return false;
        }
        finally
        {
            if (_navigation == request)
            {
                _navigation = null;
                AcceptButton.IsEnabled = true;
                BackButton.IsEnabled = _history.Count > 0;
                UpButton.IsEnabled = _folder != null && Path.GetDirectoryName(_folder) != null;
            }
        }
    }

    private static FilePickerEntry[] ReadEntries(string path, string pattern, bool showHidden, CancellationToken cancellation)
    {
        var options = new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = true };
        var entries = new List<FilePickerEntry>();
        bool windows = OperatingSystem.IsWindows();
        bool mac = OperatingSystem.IsMacOS();
        string expression = windows ? FileSystemName.TranslateWin32Expression(pattern) :
            mac ? pattern.Normalize(NormalizationForm.FormC) : pattern;
        foreach (var item in new DirectoryInfo(path).EnumerateFileSystemInfos("*", options))
        {
            cancellation.ThrowIfCancellationRequested();
            try
            {
                var attributes = item.Attributes;
                if (!showHidden && IsHidden(item.Name, attributes)) continue;
                bool folder = (attributes & FileAttributes.Directory) != 0;
                bool matches = folder || (windows
                    ? FileSystemName.MatchesWin32Expression(expression, item.Name, ignoreCase: true)
                    : FileSystemName.MatchesSimpleExpression(expression,
                        mac ? item.Name.Normalize(NormalizationForm.FormC) : item.Name, ignoreCase: mac));
                if (!matches) continue;
                entries.Add(new(item.Name, item.FullName, folder, folder ? "Folder" : Path.GetExtension(item.Name), item.LastWriteTimeUtc));
            }
            catch (Exception ex) when (IsPathError(ex)) { }
        }
        return entries.ToArray();
    }

    private async void OnPlaceChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (Places.SelectedItem is not Location location) return;
        Places.SelectedItem = null;
        await NavigateAsync(location.Path);
    }

    private async void OnBack(object? sender, RoutedEventArgs e)
    {
        if (_navigation == null && _history.TryPeek(out string? path) && await NavigateAsync(path, remember: false))
        {
            _history.Pop();
            BackButton.IsEnabled = _history.Count > 0;
        }
    }

    private async void OnUp(object? sender, RoutedEventArgs e)
    {
        if (_folder != null && Path.GetDirectoryName(_folder) is { } parent) await NavigateAsync(parent);
    }

    private async void OnFolderKeyDown(object? sender, KeyEventArgs e)
    {
        if (await HandleFolderSuggestionKeyAsync(e)) return;
        if (e.Key != Key.Enter || string.IsNullOrEmpty(FolderBox.Text)) return;
        e.Handled = true;
        try { await NavigateAsync(ResolvePath(FolderBox.Text)); }
        catch (Exception ex) when (IsPathError(ex)) { Report(ex.Message); }
    }

    private async void OnFilterChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_folder != null) await NavigateAsync(_folder, remember: false);
    }

    private async void OnHiddenFilesChanged(object? sender, RoutedEventArgs e)
    {
        if (_folder != null) await NavigateAsync(_folder, remember: false, wildcard: _wildcard);
    }

    private void OnPickerKeyDown(object? sender, KeyEventArgs e)
    {
        bool toggle = OperatingSystem.IsMacOS()
            ? e.Key == Key.OemPeriod && e.KeyModifiers == (KeyModifiers.Meta | KeyModifiers.Shift)
            : e.Key == Key.H && e.KeyModifiers == KeyModifiers.Control;
        if (!toggle) return;
        HiddenFilesBox.IsChecked = HiddenFilesBox.IsChecked != true;
        e.Handled = true;
    }

    private void OnEntryChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (Entries.SelectedItem is FilePickerEntry { IsFolder: false } file) FileNameBox.Text = file.Name;
    }

    private async void OnEntryDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Visual visual && visual.FindAncestorOfType<ListBoxItem>(includeSelf: true) != null)
            await AcceptAsync(useSelection: true);
    }

    private async void OnEntryKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F2 && e.KeyModifiers == KeyModifiers.None && Entries.SelectedItem is FilePickerEntry entry)
        {
            e.Handled = true;
            await RenameEntryAsync(entry);
            return;
        }
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await AcceptAsync(useSelection: true);
    }

    private async void OnAccept(object? sender, RoutedEventArgs e) => await AcceptAsync(useSelection: false);

    private async Task AcceptAsync(bool useSelection)
    {
        if (_accepting || _navigation != null) return;
        _accepting = true;
        try
        {
            string? name = useSelection && Entries.SelectedItem is FilePickerEntry entry ? entry.Path : FileNameBox.Text;
            if (string.IsNullOrEmpty(name) || OperatingSystem.IsWindows() && string.IsNullOrWhiteSpace(name))
            {
                if (Entries.SelectedItem is FilePickerEntry { IsFolder: true } folder)
                    await NavigateAsync(folder.Path);
                else
                    Report("Enter a file name.");
                return;
            }
            string path = ResolvePath(name);
            if (await Task.Run(() => Directory.Exists(path)))
            {
                if (await NavigateAsync(path) && !_saving) FileNameBox.Clear();
                return;
            }

            string filename = Path.GetFileName(path);
            if ((filename.Contains('*') || filename.Contains('?')) &&
                (OperatingSystem.IsWindows() || !_saving && !await Task.Run(() => File.Exists(path))))
            {
                await NavigateAsync(Path.GetDirectoryName(path)!, wildcard: filename);
                return;
            }
            if (!DocumentPath.IsValidName(filename))
                throw new IOException("Enter a valid file name.");

            bool useTextExtension = FilterBox.SelectedIndex == 0;
            var target = await Task.Run(() => CheckFile(path, _saving, useTextExtension));
            if (_closed) return;
            if (_saving && target.Exists)
            {
                var overwrite = await MessageDialog.ShowAsync(this, "Confirm Save As",
                    $"Replace {Path.GetFileName(target.Path)}?", [("_No", DialogChoice.No), ("_Yes", DialogChoice.Yes)]);
                if (overwrite != DialogChoice.Yes) return;
            }
            if (!_closed)
            {
                var encoding = (EncodingBox.SelectedItem as EncodingDialog.EncodingOption)?.Type;
                Close(new Choice(target.Path, encoding, _saving && target.Exists));
            }
        }
        catch (Exception ex) when (IsPathError(ex))
        {
            if (!_closed) Report(ex.Message);
        }
        finally { _accepting = false; }
    }

    private static (string Path, bool Exists) CheckFile(string path, bool saving, bool useTextExtension)
    {
        if (useTextExtension && !Path.HasExtension(path) && !path.EndsWith('.'))
        {
            string withExtension = path + ".txt";
            if (!File.Exists(path)) path = withExtension;
        }
        if (!Directory.Exists(Path.GetDirectoryName(path)))
            throw new DirectoryNotFoundException("The destination folder was not found.");
        bool exists = File.Exists(path);
        if (!saving && !exists) throw new FileNotFoundException("The selected file was not found.");
        if (Directory.Exists(path)) throw new IOException("The name points to a folder.");
        if (saving && exists && FileService.IsReadOnlyFile(path))
            throw new IOException("The selected file is read-only.");
        return (path, exists);
    }

    private string ResolvePath(string name)
    {
        string path = OperatingSystem.IsWindows() ? Environment.ExpandEnvironmentVariables(name.Trim().Trim('"')) : name;
        return Path.GetFullPath(path, _folder ?? Environment.CurrentDirectory);
    }

    private async void OnNewFolder(object? sender, RoutedEventArgs e)
    {
        if (_folder == null || _navigation != null) return;
        string parent = _folder;
        if (await AskForNameAsync("New Folder", "Folder name", "_Create") is not { } input) return;
        try
        {
            string folder = OperatingSystem.IsWindows() ? input.Trim() : input;
            if (folder is "." or ".." || !DocumentPath.IsValidName(folder))
                throw new IOException("Enter a valid folder name.");
            string path = Path.Combine(parent, folder);
            await Task.Run(() => Directory.CreateDirectory(path));
            await NavigateAsync(path);
        }
        catch (Exception ex) when (IsPathError(ex)) { if (!_closed) Report(ex.Message); }
    }

    private Task<string?> AskForNameAsync(string title, string watermark, string action, string? initial = null, int selectionLength = 0)
    {
        var name = new TextBox { Watermark = watermark, Text = initial };
        var confirm = new Button { Content = action, IsDefault = true, Classes = { "accent" } };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        var actions = new StackPanel { Classes = { "dialog-actions" }, Children = { confirm, cancel } };
        var dialog = new Window
        {
            Title = title, Width = 340, SizeToContent = SizeToContent.Height, ShowInTaskbar = false,
            CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel { Margin = new Thickness(16), Spacing = 12, UseLayoutRounding = true, Children = { name, actions } }
        };
        dialog.Opened += (_, _) =>
        {
            name.Focus();
            name.SelectionStart = 0;
            name.SelectionEnd = selectionLength;
        };
        confirm.Click += (_, _) => dialog.Close(name.Text);
        cancel.Click += (_, _) => dialog.Close();
        return dialog.ShowDialog<string?>(this);
    }

    private static bool IsPathError(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;

    private void Report(string message)
    {
        Status.Text = message;
        Status.IsVisible = message.Length > 0;
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
}

internal sealed record FilePickerEntry(string Name, string Path, bool IsFolder, string Kind, DateTime LastWriteTimeUtc)
{
    public string Modified => LastWriteTimeUtc.ToLocalTime().ToString("g");

    public override string ToString() => Name;
}
