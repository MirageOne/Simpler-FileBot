using System.Collections.ObjectModel;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SimplerFileBot.Core;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace SimplerFileBot.WinUI;

public sealed partial class MainWindow : Window
{
    private static readonly HashSet<string> MediaExtensions = new(StringComparer.OrdinalIgnoreCase)
    { ".mkv", ".mp4", ".avi", ".mov", ".wmv", ".m4v", ".mpg", ".mpeg", ".ts", ".webm" };
    private readonly AppConfiguration _config = new();
    private readonly MetadataService _metadata = new(new HttpClient { Timeout = TimeSpan.FromSeconds(20) });
    private readonly List<RenameMove> _lastRenames = [];
    private CancellationTokenSource? _cancellation;
    private bool _initializing = true;
    private bool _busy;
    public ObservableCollection<PreviewRow> Rows { get; } = [];

    public MainWindow()
    {
        InitializeComponent();
        Navigation.SelectionChanged += Navigation_SelectionChanged;
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1180, 760));
        Navigation.SelectedItem = Navigation.MenuItems[0];
        SystemBackdrop = new MicaBackdrop();
        Root.RequestedTheme = _config.Settings.Theme == "Dark" ? ElementTheme.Dark : ElementTheme.Light;
        SourceCombo.SelectedIndex = Math.Max(0, Array.IndexOf(MetadataSources.All, _config.Settings.MetadataSource));
        ThemeCombo.SelectedIndex = _config.Settings.Theme == "Dark" ? 1 : 0;
        FilenameOnlySwitch.IsOn = _config.Settings.FilenameOnly;
        MovieFormatBox.Text = _config.Formats.Movie;
        SeriesFormatBox.Text = _config.Formats.Series;
        TmdbKeyBox.Password = _config.Keys.Tmdb;
        OmdbKeyBox.Password = _config.Keys.Omdb;
        RefreshExcluded();
        RefreshExamples();
        _initializing = false;
    }

    private void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item) return;
        var page = item.Tag?.ToString();
        RenameView.Visibility = page == "rename" ? Visibility.Visible : Visibility.Collapsed;
        FormatsView.Visibility = page == "formats" ? Visibility.Visible : Visibility.Collapsed;
        MetadataView.Visibility = page == "metadata" ? Visibility.Visible : Visibility.Collapsed;
        SettingsView.Visibility = page == "settings" ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        picker.FileTypeFilter.Add("*");
        var files = await picker.PickMultipleFilesAsync();
        await ImportAsync(files.Select(file => file.Path), false);
    }

    private async void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        picker.FileTypeFilter.Add("*");
        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null) await ImportAsync([folder.Path], true);
    }

    private void Root_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems)) e.AcceptedOperation = DataPackageOperation.Copy;
    }

    private async void Root_Drop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        var items = await e.DataView.GetStorageItemsAsync();
        await ImportAsync(items.Select(item => item.Path), true);
    }

    private async Task ImportAsync(IEnumerable<string> paths, bool includeFolders)
    {
        if (_busy) return;
        SetBusy(true);
        _cancellation = new CancellationTokenSource();
        try
        {
            var inputs = paths.ToArray();
            var token = _cancellation.Token;
            var files = await Task.Run(() =>
            {
                var result = new List<string>();
                foreach (var path in inputs)
                {
                    token.ThrowIfCancellationRequested();
                    if (File.Exists(path)) result.Add(path);
                    else if (includeFolders && Directory.Exists(path))
                        foreach (var file in Directory.EnumerateFiles(path, "*", new EnumerationOptions
                        { RecurseSubdirectories = true, IgnoreInaccessible = true }))
                        {
                            token.ThrowIfCancellationRequested();
                            if (MediaExtensions.Contains(Path.GetExtension(file))) result.Add(file);
                        }
                }
                return result;
            }, token);
            var existing = Rows.Select(row => Path.GetFullPath(row.Item.SourcePath)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var added = 0;
            foreach (var path in files)
            {
                if (!existing.Add(Path.GetFullPath(path))) continue;
                Rows.Add(new PreviewRow(MediaParser.Parse(path, _config.Settings)));
                added++;
            }
            UpdateSummary();
            ShowNotice($"Added {added} file(s). Choose a source and match metadata.", InfoBarSeverity.Success);
        }
        catch (OperationCanceledException) { ShowNotice("Import canceled.", InfoBarSeverity.Informational); }
        catch (Exception error) { ShowNotice(error.Message, InfoBarSeverity.Error); }
        finally { SetBusy(false); _cancellation?.Dispose(); _cancellation = null; }
    }

    private async void Match_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || Rows.Count == 0) return;
        var source = SelectedSource();
        if (!string.IsNullOrWhiteSpace(YearBox.Text) && (!int.TryParse(YearBox.Text, out var year) || year is < 1900 or > 2100))
        { ShowNotice("Enter a year from 1900 to 2100, or leave it blank.", InfoBarSeverity.Warning); return; }
        SetBusy(true);
        _cancellation = new CancellationTokenSource();
        try
        {
            var items = Rows.Select(row => MediaParser.Parse(row.Item.SourcePath, _config.Settings)).ToList();
            foreach (var item in items)
            {
                if (!string.IsNullOrWhiteSpace(TitleBox.Text)) item.Title = TitleBox.Text.Trim();
                if (int.TryParse(YearBox.Text, out var overrideYear)) item.Year = overrideYear;
            }
            var results = await _metadata.MatchAsync(items, source, _config.Keys, AbsoluteCheck.IsChecked == true, _cancellation.Token);
            for (var i = 0; i < Rows.Count; i++)
            {
                Rows[i].ProposedName = FormatService.BuildName(items[i], results[i], _config.Formats);
            }
            RefreshStatuses();
            ShowNotice("Match complete. Review names and status before renaming.", InfoBarSeverity.Success);
        }
        catch (OperationCanceledException) { ShowNotice("Matching canceled.", InfoBarSeverity.Informational); }
        catch (Exception error) { ShowNotice(error.Message, InfoBarSeverity.Error); }
        finally { SetBusy(false); _cancellation?.Dispose(); _cancellation = null; }
    }

    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        RefreshStatuses();
        var selected = Rows.Where(row => row.IsSelected).ToList();
        if (selected.Count == 0) { ShowNotice("Check at least one row.", InfoBarSeverity.Warning); return; }
        if (selected.Any(row => row.Status != "Ready" && row.Status != "Already named"))
        { ShowNotice("Resolve the status issues in checked rows before renaming.", InfoBarSeverity.Warning); return; }
        var entries = selected.Select(row => new RenameEntry(row.Item.SourcePath, row.ProposedName)).ToList();
        var outcome = RenameService.Execute(entries);
        if (outcome.Completed.Count > 0)
        {
            _lastRenames.Clear();
            _lastRenames.AddRange(outcome.Completed);
        }
        foreach (var move in outcome.Completed)
        {
            var row = Rows.FirstOrDefault(r => Path.GetFullPath(r.Item.SourcePath).Equals(move.From, StringComparison.OrdinalIgnoreCase));
            if (row is null) continue;
            row.Item.SourcePath = move.To;
            row.ProposedName = Path.GetFileName(move.To);
            row.RefreshOriginal();
        }
        UndoButton.IsEnabled = _lastRenames.Count > 0;
        RefreshStatuses();
        ShowNotice(outcome.Error is null ? $"Renamed {outcome.Completed.Count} file(s)." :
            $"Renamed {outcome.Completed.Count} file(s), then stopped: {outcome.Error.Message}",
            outcome.Error is null ? InfoBarSeverity.Success : InfoBarSeverity.Error);
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _lastRenames.Count == 0) return;
        var outcome = RenameService.Undo(_lastRenames);
        foreach (var move in outcome.Completed)
        {
            var row = Rows.FirstOrDefault(r => Path.GetFullPath(r.Item.SourcePath).Equals(move.From, StringComparison.OrdinalIgnoreCase));
            if (row is null) continue;
            row.Item.SourcePath = move.To;
            row.ProposedName = Path.GetFileName(move.To);
            row.RefreshOriginal();
        }
        var undone = outcome.Completed.Select(m => m.From).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _lastRenames.RemoveAll(move => undone.Contains(move.To));
        UndoButton.IsEnabled = _lastRenames.Count > 0;
        RefreshStatuses();
        ShowNotice(outcome.Error is null ? "Last rename undone." : outcome.Error.Message,
            outcome.Error is null ? InfoBarSeverity.Success : InfoBarSeverity.Error);
    }

    private void RemoveChecked_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in Rows.Where(r => r.IsSelected).ToList()) Rows.Remove(row);
        RefreshStatuses();
    }

    private void Clear_Click(object sender, RoutedEventArgs e) { Rows.Clear(); UpdateSummary(); }
    private void Cancel_Click(object sender, RoutedEventArgs e) => _cancellation?.Cancel();

    private void ProposedName_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox { Tag: PreviewRow row } box && row.ProposedName != box.Text)
        { row.ProposedName = box.Text; RefreshStatuses(); }
    }
    private void RowSelection_Changed(object sender, RoutedEventArgs e) => RefreshStatuses();

    private void RefreshStatuses()
    {
        var selected = Rows.Where(r => r.IsSelected && r.ProposedName.Length > 0).ToList();
        var entries = selected.Select(r => new RenameEntry(r.Item.SourcePath, r.ProposedName)).ToList();
        var errors = RenameService.Validate(entries);
        for (var i = 0; i < Rows.Count; i++)
        {
            var row = Rows[i];
            if (row.ProposedName.Length == 0) row.Status = "Match needed";
            else if (!row.IsSelected) row.Status = "Excluded";
            else
            {
                var entry = new RenameEntry(row.Item.SourcePath, row.ProposedName);
                row.Status = errors.TryGetValue(entry, out var error) ? error :
                    Path.GetFileName(row.Item.SourcePath).Equals(row.ProposedName, StringComparison.Ordinal) ? "Already named" : "Ready";
            }
        }
        UpdateSummary();
    }

    private void UpdateSummary() => SummaryText.Text = Rows.Count == 0 ? "No files added" :
        $"{Rows.Count} files · {Rows.Count(r => r.IsSelected && r.Status == "Ready")} ready · {Rows.Count(r => r.IsSelected && r.Status != "Ready" && r.Status != "Already named")} need review";

    private void SourceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || SourceCombo.SelectedItem is null) return;
        _config.Settings.MetadataSource = SelectedSource();
        _config.SaveSettings();
    }
    private string SelectedSource() => (SourceCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? MetadataSources.TvMaze;

    private void Format_TextChanged(object sender, TextChangedEventArgs e) { if (!_initializing) RefreshExamples(); }
    private void RefreshExamples()
    {
        try { MovieExample.Text = FormatService.Preview(MovieFormatBox.Text, MediaKind.Movie); }
        catch (ArgumentException error) { MovieExample.Text = error.Message; }
        try { SeriesExample.Text = FormatService.Preview(SeriesFormatBox.Text, MediaKind.Episode); }
        catch (ArgumentException error) { SeriesExample.Text = error.Message; }
    }
    private void SaveFormats_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            FormatService.Preview(MovieFormatBox.Text, MediaKind.Movie);
            FormatService.Preview(SeriesFormatBox.Text, MediaKind.Episode);
            _config.Formats.Movie = MovieFormatBox.Text;
            _config.Formats.Series = SeriesFormatBox.Text;
            _config.SaveFormats();
            ShowNotice("Formats saved. Match again to refresh proposed names.", InfoBarSeverity.Success);
        }
        catch (ArgumentException error) { ShowNotice(error.Message, InfoBarSeverity.Error); }
    }
    private void SaveKeys_Click(object sender, RoutedEventArgs e)
    {
        _config.Keys.Tmdb = TmdbKeyBox.Password.Trim();
        _config.Keys.Omdb = OmdbKeyBox.Password.Trim();
        _config.SaveKeys();
        ShowNotice("API keys saved.", InfoBarSeverity.Success);
    }
    private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing) return;
        _config.Settings.Theme = ThemeCombo.SelectedIndex == 1 ? "Dark" : "Light";
        Root.RequestedTheme = ThemeCombo.SelectedIndex == 1 ? ElementTheme.Dark : ElementTheme.Light;
        _config.SaveSettings();
    }
    private void FilenameOnlySwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        _config.Settings.FilenameOnly = FilenameOnlySwitch.IsOn;
        _config.SaveSettings();
        ShowNotice("Analysis setting saved. Match again to use it.", InfoBarSeverity.Informational);
    }
    private void AddExcluded_Click(object sender, RoutedEventArgs e)
    {
        var folder = ExcludedFolderBox.Text.Trim();
        if (!Directory.Exists(folder)) { ShowNotice("Choose an existing folder.", InfoBarSeverity.Warning); return; }
        folder = Path.GetFullPath(folder);
        if (!_config.Settings.ExcludedFolders.Contains(folder, StringComparer.OrdinalIgnoreCase))
            _config.Settings.ExcludedFolders.Add(folder);
        _config.SaveSettings();
        ExcludedFolderBox.Text = "";
        RefreshExcluded();
    }
    private void RemoveExcluded_Click(object sender, RoutedEventArgs e)
    {
        if (ExcludedList.SelectedItem is not string folder) return;
        _config.Settings.ExcludedFolders.Remove(folder);
        _config.SaveSettings();
        RefreshExcluded();
    }
    private void RefreshExcluded() => ExcludedList.ItemsSource = _config.Settings.ExcludedFolders.ToArray();
    private void OpenSettings_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(_config.DataDirectory) { UseShellExecute = true });

    private void SetBusy(bool value)
    {
        _busy = value;
        BusyRing.IsActive = value;
        CancelButton.IsEnabled = value;
        MatchButton.IsEnabled = !value;
        RenameButton.IsEnabled = !value;
    }
    private void ShowNotice(string message, InfoBarSeverity severity)
    {
        Notice.Message = message;
        Notice.Severity = severity;
        Notice.IsOpen = true;
    }
}
