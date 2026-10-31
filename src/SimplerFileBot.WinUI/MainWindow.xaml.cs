using System.Collections.ObjectModel;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
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
    private readonly WorkspacePalette _palette = new();
    private readonly MetadataService _metadata = new(new HttpClient { Timeout = TimeSpan.FromSeconds(20) });
    private readonly List<RenameMove> _lastRenames = [];
    private CancellationTokenSource? _cancellation;
    private bool _initializing = true;
    private bool _busy;
    private readonly Dictionary<string, (Button Button, Border Marker, TextBlock Label)> _navigationButtons = [];
    private readonly List<TextBlock> _navigationLabels = [];
    private Grid _titleBar = null!;
    private ColumnDefinition _railColumn = null!;
    private bool _railExpanded = true;
    private Grid RenameView = null!;
    private ScrollViewer FormatsView = null!;
    private ScrollViewer MetadataView = null!;
    private ScrollViewer SettingsView = null!;
    private Border Notice = null!;
    private TextBlock NoticeText = null!;
    private StackPanel EmptyState = null!;
    private Border ReviewBanner = null!;
    private TextBlock ReviewText = null!;
    private TextBlock FormatHint = null!;
    private TextBlock LocationText = null!;
    private TextBlock ToolbarCount = null!;
    private TextBlock SummaryDetail = null!;
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
    private CheckBox FilenameOnlyCheck = null!;
    private CheckBox AbsoluteCheck = null!;
    private Button CancelButton = null!;
    private Button RemoveButton = null!;
    private Button ClearButton = null!;
    private Button MatchButton = null!;
    private Button RenameButton = null!;
    private Button UndoButton = null!;
    private TextBlock BusyStatus = null!;
    private ListView PreviewList = null!;
    private ListView ExcludedList = null!;
    private readonly Dictionary<PreviewRow, Border> _rowViews = [];
    private readonly Grid Root;
    public ObservableCollection<PreviewRow> Rows { get; } = [];

    public MainWindow()
    {
        _palette.Apply(ConfiguredDarkTheme());
        Root = new Grid();
        Content = Root;
        Title = "Simpler FileBot";
        ExtendsContentIntoTitleBar = true;
        Root.AllowDrop = true;
        Root.DragOver += Root_DragOver;
        Root.Drop += Root_Drop;
        BuildUi();
        SetTitleBar(_titleBar);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1180, 760));
        SystemBackdrop = new MicaBackdrop();
        Root.RequestedTheme = RequestedElementTheme();
        Root.ActualThemeChanged += (_, _) => ApplyVisualTheme(Root.ActualTheme == ElementTheme.Dark);
        Root.Loaded += (_, _) => ApplyVisualTheme(Root.ActualTheme == ElementTheme.Dark);
        ApplyVisualTheme(ConfiguredDarkTheme());
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (File.Exists(iconPath)) AppWindow.SetIcon(iconPath);
        SourceCombo.SelectedIndex = Math.Max(0, Array.IndexOf(MetadataSources.All, _config.Settings.MetadataSource));
        ThemeCombo.SelectedIndex = Math.Max(0, Array.IndexOf(ThemeOptions.All, _config.Settings.Theme));
        FilenameOnlySwitch.IsOn = _config.Settings.FilenameOnly;
        FilenameOnlyCheck.IsChecked = _config.Settings.FilenameOnly;
        MovieFormatBox.Text = _config.Formats.Movie;
        SeriesFormatBox.Text = _config.Formats.Series;
        TmdbKeyBox.Password = _config.Keys.Tmdb;
        OmdbKeyBox.Password = _config.Keys.Omdb;
        RefreshExcluded();
        RefreshExamples();
        _initializing = false;
        UpdateSummary();
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
    private TextBlock Label(string text, double size = 14, Brush? foreground = null, bool emphasis = false) => new()
    {
        Text = text,
        FontFamily = new FontFamily("Segoe UI Variable"),
        FontSize = size,
        FontWeight = emphasis ? FontWeights.SemiBold : FontWeights.Normal,
        Foreground = foreground ?? _palette.Text,
        TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center
    };
    private TextBlock Icon(string glyph, double size = 16) => new()
    {
        Text = glyph,
        FontFamily = new FontFamily("Segoe Fluent Icons"),
        FontSize = size,
        Foreground = _palette.Muted,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center
    };
    private static Button Button(string text, RoutedEventHandler handler)
    {
        var button = new Button { Content = text, MinHeight = 36 };
        button.Click += handler;
        return button;
    }
    private Button AccentButton(string text, RoutedEventHandler handler)
    {
        var button = Button(text, handler);
        button.Background = _palette.Accent;
        button.BorderBrush = _palette.Accent;
        button.Foreground = _palette.AccentText;
        return button;
    }
    private Border Card(UIElement child) => new()
    {
        Child = child,
        Background = _palette.Surface,
        BorderBrush = _palette.Border,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8)
    };

    private void BuildUi()
    {
        Root.Background = _palette.Canvas;
        SetRows(Root, Pixels(48), Star());
        _titleBar = BuildTitleBar();
        AddAt(Root, _titleBar, 0);
        var shell = new Grid { Background = _palette.Canvas };
        _railColumn = new ColumnDefinition { Width = Pixels(208) };
        shell.ColumnDefinitions.Add(_railColumn);
        shell.ColumnDefinitions.Add(new ColumnDefinition { Width = Star() });
        AddAt(Root, shell, 1);
        var rail = new Grid { Background = _palette.Rail };
        SetRows(rail, Auto(), Star(), Auto());
        var railHeader = new Grid { Padding = new Thickness(12, 14, 12, 12) };
        var menuButton = Button("", ToggleRail_Click);
        menuButton.Content = Icon("\uE700");
        menuButton.Background = _palette.Rail;
        menuButton.BorderThickness = new Thickness(0);
        menuButton.Width = 40;
        menuButton.HorizontalAlignment = HorizontalAlignment.Left;
        AddAt(railHeader, menuButton, 0);
        AddAt(rail, railHeader, 0);

        var navigation = new StackPanel { Spacing = 4, Padding = new Thickness(8, 4, 8, 4) };
        AddNavigationButton(navigation, "Rename", "rename", "\uE8AC");
        AddNavigationButton(navigation, "Formats", "formats", "\uE8A5");
        AddNavigationButton(navigation, "Metadata", "metadata", "\uE80A");
        AddAt(rail, navigation, 1);
        var settings = new StackPanel { Padding = new Thickness(8, 4, 8, 12) };
        AddNavigationButton(settings, "Settings", "settings", "\uE713");
        AddAt(rail, settings, 2);
        AddAt(shell, new Border { Child = rail, BorderBrush = _palette.Border,
            BorderThickness = new Thickness(0, 0, 1, 0) }, 0);

        var host = new Grid { Padding = new Thickness(30, 20, 30, 20) };
        SetRows(host, Auto(), Star());
        AddAt(shell, host, 0, 1);
        var noticeContent = new Grid { ColumnSpacing = 16 };
        SetColumns(noticeContent, Star(), Auto());
        NoticeText = Label("", 13);
        AddAt(noticeContent, NoticeText, 0);
        var dismiss = Button("Dismiss", (_, _) => Notice.Visibility = Visibility.Collapsed);
        dismiss.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        dismiss.BorderThickness = new Thickness(0);
        AddAt(noticeContent, dismiss, 0, 1);
        Notice = new Border { Child = noticeContent, Visibility = Visibility.Collapsed,
            Background = _palette.WarningSurface, BorderThickness = new Thickness(3, 0, 0, 0),
            CornerRadius = new CornerRadius(4), BorderBrush = _palette.Warning,
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

    private Grid BuildTitleBar()
    {
        var bar = new Grid { Height = 48, Background = _palette.Rail,
            Padding = new Thickness(16, 0, 140, 0) };
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center };
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.png");
        if (File.Exists(iconPath))
        {
            brand.Children.Add(new Image { Source = new BitmapImage(new Uri(iconPath)),
                Width = 22, Height = 22, VerticalAlignment = VerticalAlignment.Center });
        }
        brand.Children.Add(Label("Simpler FileBot", 13, _palette.Text, true));
        bar.Children.Add(brand);
        return bar;
    }

    private ElementTheme RequestedElementTheme() => _config.Settings.Theme switch
    {
        ThemeOptions.Dark => ElementTheme.Dark,
        ThemeOptions.Light => ElementTheme.Light,
        _ => ElementTheme.Default
    };

    private bool ConfiguredDarkTheme() => _config.Settings.Theme == ThemeOptions.Dark ||
        _config.Settings.Theme == ThemeOptions.System && Application.Current.RequestedTheme == ApplicationTheme.Dark;

    private void ApplyVisualTheme(bool dark)
    {
        _palette.Apply(dark);
        var titleBar = AppWindow.TitleBar;
        titleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        titleBar.ButtonForegroundColor = _palette.Text.Color;
        titleBar.ButtonHoverBackgroundColor = _palette.Selection.Color;
        titleBar.ButtonHoverForegroundColor = _palette.Text.Color;
        titleBar.ButtonPressedBackgroundColor = _palette.Border.Color;
        titleBar.ButtonPressedForegroundColor = _palette.Text.Color;
    }

    private void AddNavigationButton(StackPanel panel, string title, string tag, string glyph)
    {
        var content = new Grid { ColumnSpacing = 10 };
        SetColumns(content, Pixels(3), Pixels(18), Star());
        var marker = new Border { Background = _palette.Accent, Visibility = Visibility.Collapsed,
            CornerRadius = new CornerRadius(2) };
        AddAt(content, marker, 0);
        AddAt(content, Icon(glyph), 0, 1);
        var label = Label(title, 14);
        AddAt(content, label, 0, 2);
        _navigationLabels.Add(label);
        var button = Button("", Navigation_Click);
        button.Content = content;
        button.Tag = tag;
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        button.Height = 42;
        button.Padding = new Thickness(8, 0, 8, 0);
        button.Background = _palette.Rail;
        button.BorderThickness = new Thickness(0);
        panel.Children.Add(button);
        _navigationButtons.Add(tag, (button, marker, label));
    }

    private void ToggleRail_Click(object sender, RoutedEventArgs e)
    {
        _railExpanded = !_railExpanded;
        _railColumn.Width = Pixels(_railExpanded ? 208 : 64);
        foreach (var label in _navigationLabels)
            label.Visibility = _railExpanded ? Visibility.Visible : Visibility.Collapsed;
    }

    private Grid BuildRenameView()
    {
        var page = new Grid { RowSpacing = 18 };
        SetRows(page, Auto(), Auto(), Star(), Auto());
        var heading = new Grid();
        SetColumns(heading, Star(), Auto());
        var title = new StackPanel { Spacing = 2 };
        title.Children.Add(Label("Rename files", 30, _palette.Text, true));
        title.Children.Add(Label("Match multiple series together. Preview every change.", 12, _palette.Muted));
        AddAt(heading, title, 0);
        UndoButton = Button("↶  Undo last rename", Undo_Click);
        UndoButton.IsEnabled = false;
        UndoButton.VerticalAlignment = VerticalAlignment.Top;
        AddAt(heading, UndoButton, 0, 1);
        AddAt(page, heading, 0);

        var toolbar = new Grid();
        SetColumns(toolbar, Star(), Auto());
        var addCommands = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        addCommands.Children.Add(AccentButton("＋  Add files", AddFiles_Click));
        addCommands.Children.Add(Button("▱  Add folder", AddFolder_Click));
        RemoveButton = Button("Remove", RemoveChecked_Click);
        addCommands.Children.Add(RemoveButton);
        ClearButton = Button("Clear", Clear_Click);
        addCommands.Children.Add(ClearButton);
        BusyStatus = Label("Working…", 12, _palette.Muted);
        BusyStatus.Visibility = Visibility.Collapsed;
        addCommands.Children.Add(BusyStatus);
        AddAt(toolbar, addCommands, 0);
        ToolbarCount = Label("0 files added", 12, _palette.Muted);
        AddAt(toolbar, ToolbarCount, 0, 1);
        AddAt(page, toolbar, 1);

        var workspace = new Grid();
        SetRows(workspace, Auto(), Auto(), Star(), Auto(), Auto());
        var options = new StackPanel { Spacing = 12, Padding = new Thickness(18, 12, 18, 14) };
        var sourceOptions = new Grid { ColumnSpacing = 12 };
        SetColumns(sourceOptions, Star(2), Star(3), Pixels(100), Auto());
        SourceCombo = new ComboBox { Header = "Metadata source", HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var source in MetadataSources.All) SourceCombo.Items.Add(new ComboBoxItem { Content = source });
        SourceCombo.SelectionChanged += SourceCombo_SelectionChanged;
        AddAt(sourceOptions, SourceCombo, 0);
        TitleBox = new TextBox { Header = "Title override (all files)", PlaceholderText = "Blank: detect each title" };
        AddAt(sourceOptions, TitleBox, 0, 1);
        YearBox = new TextBox { Header = "Year override", PlaceholderText = "Auto" };
        AddAt(sourceOptions, YearBox, 0, 2);
        MatchButton = Button("✧  Match files", Match_Click);
        MatchButton.VerticalAlignment = VerticalAlignment.Bottom;
        AddAt(sourceOptions, MatchButton, 0, 3);
        options.Children.Add(sourceOptions);
        var formatRow = new Grid();
        SetColumns(formatRow, Star(), Auto());
        FormatHint = Label("", 12, _palette.Muted);
        AddAt(formatRow, FormatHint, 0);
        var editFormat = Button("Edit format", (_, _) => ShowSection("formats"));
        editFormat.Foreground = _palette.Accent;
        editFormat.Background = _palette.Surface;
        editFormat.BorderThickness = new Thickness(0);
        AddAt(formatRow, editFormat, 0, 1);
        options.Children.Add(formatRow);
        var checkRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
        AbsoluteCheck = new CheckBox { Content = "Absolute episode order" };
        checkRow.Children.Add(AbsoluteCheck);
        FilenameOnlyCheck = new CheckBox { Content = "Analyze filenames only" };
        FilenameOnlyCheck.Checked += FilenameOnlyCheck_Changed;
        FilenameOnlyCheck.Unchecked += FilenameOnlyCheck_Changed;
        checkRow.Children.Add(FilenameOnlyCheck);
        options.Children.Add(checkRow);
        AddAt(workspace, options, 0);

        var headings = new Grid { Background = _palette.SurfaceAlt,
            Padding = new Thickness(16, 9, 16, 9), ColumnSpacing = 12 };
        SetColumns(headings, Pixels(36), Star(2), Star(2), Pixels(140));
        AddAt(headings, Label("✓", 12, _palette.Muted), 0);
        AddAt(headings, Label("Original filename", 12, _palette.Muted), 0, 1);
        AddAt(headings, Label("New filename", 12, _palette.Muted), 0, 2);
        AddAt(headings, Label("Status", 12, _palette.Muted), 0, 3);
        AddAt(workspace, new Border { Child = headings, BorderBrush = _palette.Border,
            BorderThickness = new Thickness(0, 1, 0, 1) }, 1);

        var preview = new Grid { Background = _palette.Surface };
        PreviewList = new ListView { SelectionMode = ListViewSelectionMode.None,
            Background = _palette.Surface, BorderThickness = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch, Visibility = Visibility.Collapsed };
        preview.Children.Add(PreviewList);
        EmptyState = new StackPanel { Spacing = 9, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20) };
        EmptyState.Children.Add(Icon("\uE8A5", 38));
        var emptyTitle = Label("No files added yet", 18, _palette.Text, true);
        emptyTitle.HorizontalAlignment = HorizontalAlignment.Center;
        EmptyState.Children.Add(emptyTitle);
        var emptyHelp = Label("Add files, choose a folder, or drop media here to begin.", 13, _palette.Muted);
        emptyHelp.TextAlignment = TextAlignment.Center;
        EmptyState.Children.Add(emptyHelp);
        var emptyAdd = AccentButton("＋  Add files", AddFiles_Click);
        emptyAdd.HorizontalAlignment = HorizontalAlignment.Center;
        emptyAdd.Margin = new Thickness(0, 6, 0, 0);
        EmptyState.Children.Add(emptyAdd);
        preview.Children.Add(EmptyState);
        AddAt(workspace, preview, 2);

        ReviewText = Label("", 12);
        ReviewBanner = new Border { Child = ReviewText, Visibility = Visibility.Collapsed,
            Background = _palette.WarningSurface, BorderBrush = _palette.Warning,
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(14, 12, 14, 12) };
        AddAt(workspace, ReviewBanner, 3);
        var location = new Grid { Padding = new Thickness(18, 11, 18, 11) };
        SetColumns(location, Star(), Auto());
        LocationText = Label("Files stay in their current folder", 12, _palette.Muted);
        AddAt(location, LocationText, 0);
        AddAt(location, Label("Existing files are never replaced", 12, _palette.Muted), 0, 1);
        AddAt(workspace, new Border { Child = location, BorderBrush = _palette.Border,
            BorderThickness = new Thickness(0, 1, 0, 0) }, 4);
        AddAt(page, Card(workspace), 2);

        var footer = new Grid();
        SetColumns(footer, Star(), Auto());
        var summary = new StackPanel { Spacing = 1 };
        SummaryText = Label("No files added", 13);
        summary.Children.Add(SummaryText);
        SummaryDetail = Label("Existing files are never replaced.", 12, _palette.Muted);
        summary.Children.Add(SummaryDetail);
        AddAt(footer, summary, 0);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        CancelButton = Button("Cancel", Cancel_Click);
        CancelButton.IsEnabled = false;
        actions.Children.Add(CancelButton);
        RenameButton = AccentButton("Rename checked files", Rename_Click);
        RenameButton.IsEnabled = false;
        actions.Children.Add(RenameButton);
        AddAt(footer, actions, 0, 1);
        AddAt(page, footer, 3);
        return page;
    }

    private StackPanel PageStack(string title, string subtitle, out StackPanel fields)
    {
        var page = new StackPanel { Spacing = 22, MaxWidth = 800,
            HorizontalAlignment = HorizontalAlignment.Left };
        var heading = new StackPanel { Spacing = 3 };
        heading.Children.Add(Label(title, 30, _palette.Text, true));
        heading.Children.Add(Label(subtitle, 12, _palette.Muted));
        page.Children.Add(heading);
        fields = new StackPanel { Spacing = 16, Padding = new Thickness(22) };
        page.Children.Add(Card(fields));
        return page;
    }
    private StackPanel BuildFormatsView()
    {
        var stack = PageStack("Formats", "Choose how movies and episodes are named.", out var fields);
        fields.Children.Add(Label("Extensions are retained automatically. Use the placeholders to build each filename.",
            13, _palette.Muted));
        MovieFormatBox = new TextBox { Header = "Movie format" };
        MovieFormatBox.TextChanged += Format_TextChanged;
        fields.Children.Add(MovieFormatBox);
        fields.Children.Add(Label("{movie_name}   {year}", 12, _palette.Muted));
        MovieExample = Label("", 13);
        fields.Children.Add(MovieExample);
        SeriesFormatBox = new TextBox { Header = "Series format" };
        SeriesFormatBox.TextChanged += Format_TextChanged;
        fields.Children.Add(SeriesFormatBox);
        fields.Children.Add(Label("{series_name}   {year}   {season_number}   {episode_number}   {episode_title}",
            12, _palette.Muted));
        SeriesExample = Label("", 13);
        fields.Children.Add(SeriesExample);
        var save = AccentButton("Save formats", SaveFormats_Click);
        save.HorizontalAlignment = HorizontalAlignment.Left;
        fields.Children.Add(save);
        return stack;
    }
    private StackPanel BuildMetadataView()
    {
        var stack = PageStack("Metadata", "Connect the sources you use to match media.", out var fields);
        fields.Children.Add(Label("TVMaze and local matching need no key. TheMovieDB and OMDB keys are stored on this PC.",
            13, _palette.Muted));
        TmdbKeyBox = new PasswordBox { Header = "TheMovieDB API key" };
        fields.Children.Add(TmdbKeyBox);
        OmdbKeyBox = new PasswordBox { Header = "OMDB API key" };
        fields.Children.Add(OmdbKeyBox);
        var save = AccentButton("Save API keys", SaveKeys_Click);
        save.HorizontalAlignment = HorizontalAlignment.Left;
        fields.Children.Add(save);
        return stack;
    }
    private StackPanel BuildSettingsView()
    {
        var stack = PageStack("Settings", "Adjust the workspace and filename analysis.", out var fields);
        ThemeCombo = new ComboBox { Header = "Theme" };
        ThemeCombo.Items.Add(new ComboBoxItem { Content = "Follow system" });
        ThemeCombo.Items.Add(new ComboBoxItem { Content = "Light" });
        ThemeCombo.Items.Add(new ComboBoxItem { Content = "Dark" });
        ThemeCombo.SelectionChanged += ThemeCombo_SelectionChanged;
        fields.Children.Add(ThemeCombo);
        FilenameOnlySwitch = new ToggleSwitch { Header = "Analyze filenames only" };
        FilenameOnlySwitch.Toggled += FilenameOnlySwitch_Toggled;
        fields.Children.Add(FilenameOnlySwitch);
        fields.Children.Add(Label("Excluded folders are ignored when finding titles and years from parent folders.",
            13, _palette.Muted));
        var folderOptions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        ExcludedFolderBox = new TextBox { PlaceholderText = @"C:\Media\Unsorted", Width = 440 };
        folderOptions.Children.Add(ExcludedFolderBox);
        folderOptions.Children.Add(Button("Add excluded folder", AddExcluded_Click));
        fields.Children.Add(folderOptions);
        ExcludedList = new ListView { Height = 150, SelectionMode = ListViewSelectionMode.Single,
            Background = _palette.SurfaceAlt, BorderBrush = _palette.Border,
            BorderThickness = new Thickness(1) };
        fields.Children.Add(ExcludedList);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(Button("Remove selected folder", RemoveExcluded_Click));
        actions.Children.Add(Button("Open settings folder", OpenSettings_Click));
        fields.Children.Add(actions);
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

    private Border BuildPreviewRow(PreviewRow row)
    {
        var view = new Grid { Padding = new Thickness(16, 5, 16, 5), ColumnSpacing = 12, MinHeight = 56 };
        SetColumns(view, Pixels(36), Star(2), Star(2), Pixels(140));
        var container = new Border { Child = view, Background = _palette.Surface,
            BorderBrush = _palette.Border, BorderThickness = new Thickness(0, 0, 0, 1) };
        var check = new CheckBox { IsChecked = row.IsSelected, Tag = row,
            VerticalAlignment = VerticalAlignment.Center };
        check.Checked += RowSelection_Changed;
        check.Unchecked += RowSelection_Changed;
        AddAt(view, check, 0);
        var originalCell = new Grid { ColumnSpacing = 8 };
        SetColumns(originalCell, Pixels(18), Star());
        AddAt(originalCell, Icon("\uE8A5", 14), 0);
        var original = Label(row.OriginalName, 13, _palette.Muted);
        original.TextTrimming = TextTrimming.CharacterEllipsis;
        ToolTipService.SetToolTip(original, row.Item.SourcePath);
        var originalDetails = new StackPanel { Spacing = 1 };
        originalDetails.Children.Add(original);
        var detectedTitle = Label("", 11, _palette.Muted);
        detectedTitle.TextTrimming = TextTrimming.CharacterEllipsis;
        void UpdateDetectedTitle() => detectedTitle.Text = row.Item.Kind == MediaKind.Episode
            ? $"Series: {(string.IsNullOrWhiteSpace(row.DisplayTitle) ? "Not detected" : row.DisplayTitle)}"
            : $"Movie: {(string.IsNullOrWhiteSpace(row.DisplayTitle) ? "Not detected" : row.DisplayTitle)}";
        UpdateDetectedTitle();
        originalDetails.Children.Add(detectedTitle);
        AddAt(originalCell, originalDetails, 0, 1);
        AddAt(view, originalCell, 0, 1);
        var proposed = new TextBox { Text = row.ProposedName, Tag = row,
            VerticalAlignment = VerticalAlignment.Center };
        proposed.TextChanged += ProposedName_TextChanged;
        AddAt(view, proposed, 0, 2);
        var status = Label(row.Status, 12);
        AddAt(view, status, 0, 3);
        void UpdateStatus()
        {
            var ready = row.Status == "Ready";
            var named = row.Status == "Already named";
            var review = !ready && !named && row.Status != "Excluded" && row.Status != "Match needed";
            status.Text = ready ? "✓  Ready" : review ? "⚠  " + row.Status : row.Status;
            status.Foreground = ready ? _palette.Success : review ? _palette.Warning : _palette.Muted;
            container.Background = review ? _palette.WarningSurface : _palette.Surface;
        }
        row.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(PreviewRow.OriginalName)) original.Text = row.OriginalName;
            if (args.PropertyName == nameof(PreviewRow.DisplayTitle)) UpdateDetectedTitle();
            if (args.PropertyName == nameof(PreviewRow.ProposedName) && proposed.Text != row.ProposedName)
                proposed.Text = row.ProposedName;
            if (args.PropertyName == nameof(PreviewRow.Status)) UpdateStatus();
            if (args.PropertyName == nameof(PreviewRow.IsSelected)) check.IsChecked = row.IsSelected;
        };
        UpdateStatus();
        return container;
    }

    private void Navigation_Click(object sender, RoutedEventArgs args)
    {
        if (sender is Button button && button.Tag is string page) ShowSection(page);
    }

    private void ShowSection(string page)
    {
        foreach (var (tag, navigation) in _navigationButtons)
        {
            var selected = tag == page;
            navigation.Button.Background = selected ? _palette.Selection : _palette.Rail;
            navigation.Marker.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
            navigation.Label.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
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
                Rows[i].DisplayTitle = results[i].Title ?? items[i].Title;
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

    private void UpdateSummary()
    {
        var total = Rows.Count;
        FormatHint.Text = $"Format: {(Rows.FirstOrDefault()?.Item.Kind == MediaKind.Movie ? _config.Formats.Movie : _config.Formats.Series)}";
        var seriesCount = Rows.Where(row => row.Item.Kind == MediaKind.Episode && !string.IsNullOrWhiteSpace(row.DisplayTitle))
            .Select(row => row.DisplayTitle.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var ready = Rows.Count(row => row.IsSelected && row.Status == "Ready");
        var selected = Rows.Count(row => row.IsSelected);
        var review = Rows.Count(row => row.IsSelected && row.Status is not ("Ready" or "Already named" or "Match needed"));
        ToolbarCount.Text = total == 0 ? "No files added" : seriesCount > 1
            ? $"{total} files · {seriesCount} series" : $"{total} files added";
        SummaryText.Text = ready == 0 ? total == 0 ? "No files added" : "No files ready to rename" :
            $"{ready} {(ready == 1 ? "file" : "files")} ready to rename";
        SummaryDetail.Text = review > 0 ? $"{review} need review before renaming." :
            "Existing files are never replaced.";
        RenameButton.Content = ready > 0 ? $"Rename {ready} {(ready == 1 ? "file" : "files")}" : "Rename checked files";
        RenameButton.IsEnabled = !_busy && ready > 0;
        MatchButton.IsEnabled = !_busy && total > 0;
        RemoveButton.IsEnabled = !_busy && selected > 0;
        ClearButton.IsEnabled = !_busy && total > 0;
        EmptyState.Visibility = total == 0 ? Visibility.Visible : Visibility.Collapsed;
        PreviewList.Visibility = total == 0 ? Visibility.Collapsed : Visibility.Visible;
        ReviewBanner.Visibility = review > 0 ? Visibility.Visible : Visibility.Collapsed;
        ReviewText.Text = review == 1 ? "1 file needs review. Resolve its status before renaming." :
            $"{review} files need review. Resolve their status before renaming.";
        var folders = Rows.Select(row => Path.GetDirectoryName(row.Item.SourcePath) ?? "")
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        LocationText.Text = folders.Length == 1 ? $"▱  {folders[0]}" :
            folders.Length > 1 ? $"▱  {folders.Length} source folders" : "Files stay in their current folder";
    }

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
            UpdateSummary();
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
        _config.Settings.Theme = ThemeOptions.All[Math.Max(0, ThemeCombo.SelectedIndex)];
        Root.RequestedTheme = RequestedElementTheme();
        ApplyVisualTheme(ConfiguredDarkTheme());
        _config.SaveSettings();
    }
    private void FilenameOnlyCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!_initializing) SetFilenameOnly(FilenameOnlyCheck.IsChecked == true);
    }
    private void FilenameOnlySwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_initializing) SetFilenameOnly(FilenameOnlySwitch.IsOn);
    }
    private void SetFilenameOnly(bool value)
    {
        if (_config.Settings.FilenameOnly == value) return;
        _config.Settings.FilenameOnly = value;
        if (FilenameOnlySwitch.IsOn != value) FilenameOnlySwitch.IsOn = value;
        if (FilenameOnlyCheck.IsChecked != value) FilenameOnlyCheck.IsChecked = value;
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
        UndoButton.IsEnabled = !value && _lastRenames.Count > 0;
        UpdateSummary();
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
        Notice.Background = severity is InfoBarSeverity.Warning or InfoBarSeverity.Error ?
            _palette.WarningSurface : _palette.SurfaceAlt;
        Notice.BorderBrush = severity switch
        {
            InfoBarSeverity.Warning or InfoBarSeverity.Error => _palette.Warning,
            InfoBarSeverity.Success => _palette.Success,
            _ => _palette.Accent
        };
        Notice.Visibility = Visibility.Visible;
    }
}
