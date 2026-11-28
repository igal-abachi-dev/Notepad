using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Themes.Fluent;
using Avalonia.VisualTree;
using Notepad.NeoEdit;
using NotepadAvalonia.Models;
using NotepadAvalonia.Services;
using NotepadAvalonia.ViewModels;
using NotepadAvalonia.Views;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Notepad.Tests;

public class DraftRecoveryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Store_PreservesTheSaveTargetAndStillReadsVersionsWithoutOne(bool trackTarget)
    {
        using var folder = new DraftFolder();
        string file = Path.Combine(folder.Path, "notes.txt");
        await File.WriteAllTextAsync(file, "before");
        var opened = await new FileService().ReadDocumentAsync(file);
        var version = trackTarget ? opened.Version : opened.Version with { ResolvedPath = null };
        using var lease = new DraftStore(folder.Path).Create();
        await lease.WriteAsync(Snapshot("edit") with { Path = file, SavedFileVersion = version }, 1);

        var recovered = Assert.IsType<DraftSnapshot>(lease.Read());
        Assert.Equal("edit", recovered.Content);
        Assert.Equal(version, recovered.SavedFileVersion);
        Assert.Equal(version.ResolvedPath, recovered.SavedFileVersion!.ResolvedPath);
    }

    [Fact]
    public async Task Store_RetainsTheSuggestedExtensionForACopiedSelection()
    {
        using var folder = new DraftFolder();
        var store = new DraftStore(folder.Path);
        var lease = store.Create();
        await lease.WriteAsync(Snapshot("Get-Date\r\n") with { SuggestedExtension = ".ps1" }, 0);
        await lease.ReleaseAsync();
        using var claimed = Assert.IsType<DraftLease>(store.TryClaim(lease.Id));
        var recovered = Assert.IsType<DraftSnapshot>(claimed.Read());
        Assert.Equal(".ps1", recovered.SuggestedExtension);
        Assert.Null(recovered.Path);
        Assert.Equal("Get-Date\r\n", recovered.Content);
    }

    [Fact]
    public async Task Store_RetainsMultipleDraftsAndRefusesToClaimAnActiveOne()
    {
        using var folder = new DraftFolder();
        var writer = new DraftStore(folder.Path);
        var other = new DraftStore(folder.Path);
        var first = writer.Create();
        var second = writer.Create();
        try
        {
            await Task.WhenAll(first.WriteAsync(Snapshot("first"), 1), second.WriteAsync(Snapshot("second"), 1));
            Assert.Null(other.TryClaim(first.Id));
            Assert.Empty(await other.DiscoverAsync());
        }
        finally
        {
            await first.ReleaseAsync();
            await second.ReleaseAsync();
        }

        var drafts = await other.DiscoverAsync();
        Assert.Equal(2, drafts.Count);
        using var claimed = Assert.IsType<DraftLease>(other.TryClaim(first.Id));
        Assert.Equal("first", claimed.Read()!.Content);
        Assert.Null(writer.TryClaim(first.Id));
    }

    [Fact]
    public async Task Store_RejectsCorruptionFallsBackAndDoesNotResurrectADiscardedDraft()
    {
        using var folder = new DraftFolder();
        var store = new DraftStore(folder.Path);
        var lease = store.Create();
        Guid id = lease.Id;
        await lease.WriteAsync(Snapshot("\uFEFFalpha\r\n\0\uD83D\uDE00"), 1);
        await lease.WriteAsync(Snapshot("new version"), 2);
        await lease.WriteAsync(Snapshot("outdated"), 1);
        Assert.Equal("new version", lease.Read()!.Content);
        await lease.ReleaseAsync();

        await File.WriteAllTextAsync(Path.Combine(folder.Path, id.ToString("N") + ".draft"), "truncated");
        using var restored = Assert.IsType<DraftLease>(store.TryClaim(id));
        Assert.Equal("\uFEFFalpha\r\n\0\uD83D\uDE00", restored.Read()!.Content);
        await restored.WriteAsync(Snapshot("recovered"), 1);
        await restored.DiscardAsync();
        await restored.ReleaseAsync();
        Assert.Empty(await store.DiscoverAsync());
    }

    [AvaloniaFact]
    public async Task Window_CancelKeepsTheDraftAndNextLaunchRecoversOnlyViaMenu()
    {
        using var folder = new DraftFolder();
        var store = new DraftStore(folder.Path);
        var firstModel = CreateModel(store, folder.Path);
        var first = Show(firstModel);
        MainWindow? second = null;
        try
        {
            var editor = Assert.IsType<NeoEditor>(firstModel.EditorControl);
            Assert.True(editor.IsFocused);
            Assert.Empty(editor.Text);
            first.KeyTextInput("unsaved text");
            editor.SetSelection(12, -4);

            await WaitForAsync(() => Directory.GetFiles(folder.Path, "*.draft").Length == 1);
            Assert.Empty(await store.DiscoverAsync());

            var closed = new TaskCompletionSource();
            first.Closed += (_, _) => closed.SetResult();
            first.Close();
            var prompt = Assert.Single(first.OwnedWindows);
            prompt.GetVisualDescendants().OfType<Button>().Single(button => button.IsCancel)
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(first.IsVisible);
            Assert.Equal("unsaved text", editor.Text);
            Assert.Single(Directory.GetFiles(folder.Path, "*.draft"));

            // An operating-system close checkpoints drafts without treating it as explicit discard.
            await firstModel.CheckpointForSystemShutdownAsync();
            first.CloseApproved();
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var nextModel = CreateModel(new DraftStore(folder.Path), folder.Path);
            second = Show(nextModel);
            var nextEditor = Assert.IsType<NeoEditor>(nextModel.EditorControl);
            Assert.True(nextEditor.IsFocused);
            Assert.Empty(nextEditor.Text);
            Assert.Empty(second.OwnedWindows);
            await WaitForAsync(async () => (await store.DiscoverAsync()).Count == 1);

            var recovery = nextModel.RecoverDraftsCommand.ExecuteAsync(null);
            await WaitForAsync(() => second.OwnedWindows.OfType<RecoverDraftDialog>().Any());
            var picker = Assert.Single(second.OwnedWindows.OfType<RecoverDraftDialog>());
            var restore = picker.GetVisualDescendants().OfType<Button>().Single(button => button.IsDefault);
            restore.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await recovery.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("unsaved text", nextEditor.Text);
            Assert.Equal((12, 8), (nextEditor.SelectionAnchor, nextEditor.CaretOffset));
            Assert.True(nextModel.Document.IsUntitled);
            Assert.True(nextModel.Document.IsModified);
        }
        finally
        {
            if (first.IsVisible) await CloseAsync(first, preserveDraft: true);
            if (second?.IsVisible == true) await CloseAsync(second, preserveDraft: true);
            await WaitForAsync(async () => (await store.DiscoverAsync()).Count == 1);
        }
    }

    [AvaloniaFact]
    public async Task Window_DontSaveOnClassicClose_RemovesTheRecoveryDraft()
    {
        using var folder = new DraftFolder();
        var store = new DraftStore(folder.Path);
        var model = CreateModel(store, folder.Path);
        var window = Show(model);
        try
        {
            window.KeyTextInput("discard this");
            Assert.True(await model.CheckpointDraftAsync());
            Assert.Single(Directory.GetFiles(folder.Path, "*.draft"));
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            window.Closed += (_, _) => closed.SetResult();
            window.Close();
            var prompt = Assert.Single(window.OwnedWindows);
            prompt.GetVisualDescendants().OfType<Button>().Single(button => !button.IsDefault && !button.IsCancel)
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Empty(await store.DiscoverAsync());
            Assert.Empty(Directory.GetFiles(folder.Path, "*.draft"));
        }
        finally
        {
            if (window.IsVisible) await CloseAsync(window, preserveDraft: true);
        }
    }

    [AvaloniaFact]
    public async Task Window_UndoToCleanThenClose_DoesNotBringBackDiscardedText()
    {
        using var folder = new DraftFolder();
        var store = new DraftStore(folder.Path);
        var model = CreateModel(store, folder.Path);
        var window = Show(model);
        try
        {
            var editor = Assert.IsType<NeoEditor>(model.EditorControl);
            window.KeyTextInput("temporary");
            await WaitForAsync(() => Directory.GetFiles(folder.Path, "*.draft").Length == 1);
            editor.Undo();
            Assert.False(model.Document.IsModified);
            await CloseAsync(window);
            Assert.Empty(Directory.GetFiles(folder.Path, "*.draft"));
            Assert.Empty(await store.DiscoverAsync());
        }
        finally
        {
            if (window.IsVisible) await CloseAsync(window);
        }
    }

    private static DraftSnapshot Snapshot(string text) =>
        new(text, null, FileEncodingType.UTF8, LineEndingStyle.CRLF, 0, 0, null, DateTimeOffset.UtcNow);

    private static MainWindowViewModel CreateModel(DraftStore store, string directory) =>
        new(new FileService(), new SearchService(), new SettingsService(Path.Combine(directory, "settings.json")),
            new PrintService(), draftStore: store);

    private static MainWindow Show(MainWindowViewModel model)
    {
        var window = new MainWindow { DataContext = model };
        window.Styles.Add(new FluentTheme());
        window.Show();
        window.UpdateLayout();
        return window;
    }

    private static async Task CloseAsync(MainWindow window, bool preserveDraft = false)
    {
        var closed = new TaskCompletionSource();
        window.Closed += (_, _) => closed.SetResult();
        if (preserveDraft)
        {
            await window.ViewModel.CheckpointForSystemShutdownAsync();
            window.CloseApproved();
        }
        else
            window.Close();
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static Task WaitForAsync(Func<bool> predicate) => WaitForAsync(() => Task.FromResult(predicate()));

    private static async Task WaitForAsync(Func<Task<bool>> predicate)
    {
        var until = DateTime.UtcNow.AddSeconds(8);
        while (!await predicate())
        {
            if (DateTime.UtcNow >= until) throw new TimeoutException();
            await Task.Delay(20);
        }
    }

    private sealed class DraftFolder : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("notepad-draft-tests-").FullName;
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
