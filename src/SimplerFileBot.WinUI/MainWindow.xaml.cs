using System.Collections.ObjectModel;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Text;
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
    private readonly Dictionary<string, Button> _navigationButtons = [];
    private Grid RenameView = null!;
    private ScrollViewer FormatsView = null!;
    private ScrollViewer MetadataView = null!;
    private ScrollViewer SettingsView = null!;
    private Border Notice = null!;
    private TextBlock NoticeText = null!;
    private ComboBox SourceCombo = null!;
    private ComboBox ThemeCombo = null!;
    private TextBox TitleBox = null!;
    private TextBox YearBox = null!;
    private TextBox MovieFormatBox = null!;
    private TextBox SeriesFormatBox = null!;
    private TextBox ExcludedFolderBox = null!;
    private TextBlock MovieExample = null!;
    private TextBlock SeriesExample = null!;
    private TextBlock SummaryText = null!;
    private PasswordBox TmdbKeyBox = null!;
    private PasswordBox OmdbKeyBox = null!;
    private ToggleSwitch FilenameOnlySwitch = null!;
    private CheckBox AbsoluteCheck = null!;
    private Button CancelButton = null!;
    private Button MatchButton = null!;
    private Button RenameButton = null!;
    private Button UndoButton = null!;
    private TextBlock BusyStatus = null!;
    private ListView PreviewList = null!;
    private ListView ExcludedList = null!;
    private readonly Dictionary<PreviewRow, Grid> _rowViews = [];
    private readonly Grid Root;
    public ObservableCollection<PreviewRow> Rows { get; } = [];

    public MainWindow()
    {
        Root = new Grid();
        Content = Root;
        Title = "Simpler FileBot";
        Root.AllowDrop = true;
        Root.DragOver += Root_DragOver;
        Root.Drop += Root_Drop;
        BuildUi();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1180, 760));
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

    private static GridLength Pixels(double size) => new(size, GridUnitType.Pixel);
    private static GridLength Star(double weight = 1) => new(weight, GridUnitType.Star);
    private static GridLength Auto() => new(1, GridUnitType.Auto);
    private static void SetRows(Grid grid, params GridLength[] heights)
    {
        foreach (var height in heights) grid.RowDefinitions.Add(new RowDefinition { Height = height });
    }
    private static void SetColumns(Grid grid, params GridLength[] widths)
    {
        foreach (var width in widths) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
    }
    private static void AddAt(Grid grid, FrameworkElement child, int row, int column = 0)
    {
        Grid.SetRow(child, row);
        Grid.SetColumn(child, column);
        grid.Children.Add(child);
    }
    private static TextBlock Label(string text, double size = 14) => new()
    { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private static Button Button(string text, RoutedEventHandler handler)
    {
        var button = new Button { Content = text };
        button.Click += handler;
        return button;
    }

    private void BuildUi()
    {
        var shell = new Grid();
        SetColumns(shell, Pixels(200), Star());
        Root.Children.Add(shell);
        var rail = new StackPanel { Spacing = 8, Padding = new Thickness(16, 24, 16, 16) };
        rail.Children.Add(Label("Simpler FileBot", 20));
        foreach (var (title, tag) in new[] { ("Rename", "rename"), ("Formats", "formats"),
                     ("Metadata", "metadata"), ("Settings", "settings") })
        {
            var button = Button(title, Navigation_Click);
            button.Tag = tag;
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            rail.Children.Add(button);
            _navigationButtons.Add(tag, button);
        }
        AddAt(shell, rail, 0);

        var host = new Grid { Padding = new Thickness(28, 20, 28, 20) };
        SetRows(host, Auto(), Star());
        AddAt(shell, host, 0, 1);
        var noticeContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        NoticeText = Label("");
        noticeContent.Children.Add(NoticeText);
        noticeContent.Children.Add(Button("Dismiss", (_, _) => Notice.Visibility = Visibility.Collapsed));
        Notice = new Border { Child = noticeContent, Visibility = Visibility.Collapsed,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
            BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Gray),
            Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 0, 12) };
        AddAt(host, Notice, 0);

        RenameView = BuildRenameView();
        AddAt(host, RenameView, 1);
        FormatsView = new ScrollViewer { Visibility = Visibility.Collapsed,
            Content = BuildFormatsView() };
        AddAt(host, FormatsView, 1);
        MetadataView = new ScrollViewer { Visibility = Visibility.Collapsed,
            Content = BuildMetadataView() };
        AddAt(host, MetadataView, 1);
        SettingsView = new ScrollViewer { Visibility = Visibility.Collapsed,
            Content = BuildSettingsView() };
        AddAt(host, SettingsView, 1);
        ShowSection("rename");
    }

    private Grid BuildRenameView()
    {
        var page = new Grid { RowSpacing = 16 };
        SetRows(page, Auto(), Auto(), Auto(), Auto(), Star(), Auto());
        var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        heading.Children.Add(Label("Rename", 28));
        heading.Children.Add(Label("Add media, match titles, review every destination, then rename."));
        AddAt(page, heading, 0);

        var addCommands = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        addCommands.Children.Add(Button("Add files", AddFiles_Click));
        addCommands.Children.Add(Button("Add folder", AddFolder_Click));
        addCommands.Children.Add(Button("Remove checked", RemoveChecked_Click));
        addCommands.Children.Add(Button("Clear", Clear_Click));
        CancelButton = Button("Cancel", Cancel_Click);
        CancelButton.IsEnabled = false;
        addCommands.Children.Add(CancelButton);
        BusyStatus = Label("Working…");
        BusyStatus.Visibility = Visibility.Collapsed;
        addCommands.Children.Add(BusyStatus);
        AddAt(page, addCommands, 1);

        var sourceOptions = new Grid { ColumnSpacing = 12 };
        SetColumns(sourceOptions, Star(2), Star(2), Star());
        SourceCombo = new ComboBox { Header = "Metadata source", HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var source in MetadataSources.All) SourceCombo.Items.Add(new ComboBoxItem { Content = source });
        SourceCombo.SelectionChanged += SourceCombo_SelectionChanged;
        AddAt(sourceOptions, SourceCombo, 0);
        TitleBox = new TextBox { Header = "Title override (optional)", PlaceholderText = "Use detected title" };
        AddAt(sourceOptions, TitleBox, 0, 1);
        YearBox = new TextBox { Header = "Year override (optional)", PlaceholderText = "Auto" };
        AddAt(sourceOptions, YearBox, 0, 2);
        AddAt(page, sourceOptions, 2);

        var renameCommands = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        AbsoluteCheck = new CheckBox { Content = "Absolute TV episode order" };
        renameCommands.Children.Add(AbsoluteCheck);
        MatchButton = Button("Match metadata", Match_Click);
        renameCommands.Children.Add(MatchButton);
        RenameButton = Button("Rename checked", Rename_Click);
        renameCommands.Children.Add(RenameButton);
        UndoButton = Button("Undo last rename", Undo_Click);
        UndoButton.IsEnabled = false;
        renameCommands.Children.Add(UndoButton);
        AddAt(page, renameCommands, 3);

        var preview = new Grid { RowSpacing = 8 };
        SetRows(preview, Auto(), Star());
        var headings = new Grid { Padding = new Thickness(14, 0, 14, 0), ColumnSpacing = 12 };
        SetColumns(headings, Pixels(44), Star(2), Star(2), Pixels(160));
        AddAt(headings, Label("ORIGINAL"), 0, 1);
        AddAt(headings, Label("NEW NAME"), 0, 2);
        AddAt(headings, Label("STATUS"), 0, 3);
        AddAt(preview, headings, 0);
        PreviewList = new ListView { SelectionMode = ListViewSelectionMode.None };
        AddAt(preview, PreviewList, 1);
        AddAt(page, preview, 4);
        SummaryText = Label("No files added");
        SummaryText.Opacity = 0.7;
        AddAt(page, SummaryText, 5);
        return page;
    }

    private StackPanel PageStack(string title)
    {
        var stack = new StackPanel { Spacing = 16, MaxWidth = 720,
            HorizontalAlignment = HorizontalAlignment.Left };
        stack.Children.Add(Label(title, 28));
        return stack;
    }
    private StackPanel BuildFormatsView()
    {
        var stack = PageStack("Formats");
        stack.Children.Add(Label("Choose filenames using the placeholders below. Extensions are retained automatically."));
        MovieFormatBox = new TextBox { Header = "Movie format" };
        MovieFormatBox.TextChanged += Format_TextChanged;
        stack.Children.Add(MovieFormatBox);
        stack.Children.Add(Label("{movie_name}  {year}"));
        MovieExample = Label("");
        stack.Children.Add(MovieExample);
        SeriesFormatBox = new TextBox { Header = "Series format" };
        SeriesFormatBox.TextChanged += Format_TextChanged;
        stack.Children.Add(SeriesFormatBox);
        stack.Children.Add(Label("{series_name}  {year}  {season_number}  {episode_number}  {episode_title}"));
        SeriesExample = Label("");
        stack.Children.Add(SeriesExample);
        stack.Children.Add(Button("Save formats", SaveFormats_Click));
        return stack;
    }
    private StackPanel BuildMetadataView()
    {
        var stack = PageStack("Metadata");
        stack.Children.Add(Label("TVMaze and local matching need no key. TheMovieDB and OMDB keys are stored on this PC."));
        TmdbKeyBox = new PasswordBox { Header = "TheMovieDB API key" };
        stack.Children.Add(TmdbKeyBox);
        OmdbKeyBox = new PasswordBox { Header = "OMDB API key" };
        stack.Children.Add(OmdbKeyBox);
        stack.Children.Add(Button("Save API keys", SaveKeys_Click));
        return stack;
    }
    private StackPanel BuildSettingsView()
    {
        var stack = PageStack("Settings");
        ThemeCombo = new ComboBox { Header = "Theme" };
        ThemeCombo.Items.Add(new ComboBoxItem { Content = "Light" });
        ThemeCombo.Items.Add(new ComboBoxItem { Content = "Dark" });
        ThemeCombo.SelectionChanged += ThemeCombo_SelectionChanged;
        stack.Children.Add(ThemeCombo);
        FilenameOnlySwitch = new ToggleSwitch { Header = "Analyze filenames only" };
        FilenameOnlySwitch.Toggled += FilenameOnlySwitch_Toggled;
        stack.Children.Add(FilenameOnlySwitch);
        stack.Children.Add(Label("Excluded folders are ignored when finding titles and years from parent folders."));
        var folderOptions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        ExcludedFolderBox = new TextBox { PlaceholderText = @"C:\Media\Unsorted", Width = 440 };
        folderOptions.Children.Add(ExcludedFolderBox);
        folderOptions.Children.Add(Button("Add excluded folder", AddExcluded_Click));
        stack.Children.Add(folderOptions);
        ExcludedList = new ListView { Height = 150, SelectionMode = ListViewSelectionMode.Single };
        stack.Children.Add(ExcludedList);
        stack.Children.Add(Button("Remove selected folder", RemoveExcluded_Click));
        stack.Children.Add(Button("Open settings folder", OpenSettings_Click));
        return stack;
    }

    private void RebuildPreview()
    {
        var remaining = Rows.ToHashSet();
        foreach (var row in _rowViews.Keys.Where(row => !remaining.Contains(row)).ToList()) _rowViews.Remove(row);
        PreviewList.Items.Clear();
        foreach (var row in Rows)
        {
            if (!_rowViews.TryGetValue(row, out var view)) _rowViews[row] = view = BuildPreviewRow(row);
            PreviewList.Items.Add(view);
        }
    }

    private Grid BuildPreviewRow(PreviewRow row)
    {
        var view = new Grid { Padding = new Thickness(8), ColumnSpacing = 12, MinHeight = 52 };
        SetColumns(view, Pixels(44), Star(2), Star(2), Pixels(160));
        var check = new CheckBox { IsChecked = row.IsSelected, Tag = row,
            VerticalAlignment = VerticalAlignment.Center };
        check.Checked += RowSelection_Changed;
        check.Unchecked += RowSelection_Changed;
        AddAt(view, check, 0);
        var original = Label(row.OriginalName);
        original.TextTrimming = TextTrimming.CharacterEllipsis;
        AddAt(view, original, 0, 1);
        var proposed = new TextBox { Text = row.ProposedName, Tag = row,
            VerticalAlignment = VerticalAlignment.Center };
        proposed.TextChanged += ProposedName_TextChanged;
        AddAt(view, proposed, 0, 2);
        var status = Label(row.Status);
        AddAt(view, status, 0, 3);
        row.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(PreviewRow.OriginalName)) original.Text = row.OriginalName;
            if (args.PropertyName == nameof(PreviewRow.ProposedName) && proposed.Text != row.ProposedName)
                proposed.Text = row.ProposedName;
            if (args.PropertyName == nameof(PreviewRow.Status)) status.Text = row.Status;
            if (args.PropertyName == nameof(PreviewRow.IsSelected)) check.IsChecked = row.IsSelected;
        };
        return view;
    }

    private void Navigation_Click(object sender, RoutedEventArgs args)
    {
        if (sender is Button button && button.Tag is string page) ShowSection(page);
    }

    private void ShowSection(string page)
    {
        foreach (var (tag, button) in _navigationButtons)
        {
            button.FontWeight = tag == page ? FontWeights.SemiBold : FontWeights.Normal;
            button.Opacity = tag == page ? 1 : 0.75;
        }
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
            RebuildPreview();
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
        RebuildPreview();
        RefreshStatuses();
    }

    private void Clear_Click(object sender, RoutedEventArgs e) { Rows.Clear(); RebuildPreview(); UpdateSummary(); }
    private void Cancel_Click(object sender, RoutedEventArgs e) => _cancellation?.Cancel();

    private void ProposedName_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox { Tag: PreviewRow row } box && row.ProposedName != box.Text)
        { row.ProposedName = box.Text; RefreshStatuses(); }
    }
    private void RowSelection_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { Tag: PreviewRow row } check) row.IsSelected = check.IsChecked == true;
        RefreshStatuses();
    }

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
        BusyStatus.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.IsEnabled = value;
        MatchButton.IsEnabled = !value;
        RenameButton.IsEnabled = !value;
    }
    private void ShowNotice(string message, InfoBarSeverity severity)
    {
        var prefix = severity switch
        {
            InfoBarSeverity.Error => "Error: ",
            InfoBarSeverity.Warning => "Check: ",
            InfoBarSeverity.Success => "Done: ",
            _ => ""
        };
        NoticeText.Text = prefix + message;
        Notice.Visibility = Visibility.Visible;
    }
}
