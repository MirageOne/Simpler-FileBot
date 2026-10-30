using System.Text.Json;
using System.Text.Json.Serialization;

namespace SimplerFileBot.Core;

public static class MetadataSources
{
    public const string TvMaze = "TVMaze";
    public const string Tmdb = "TheMovieDB";
    public const string Omdb = "OMDB";
    public const string Local = "Local metadata";
    public static readonly string[] All = [TvMaze, Tmdb, Omdb, Local];
}

public static class ThemeOptions
{
    public const string System = "System";
    public const string Light = "Light";
    public const string Dark = "Dark";
    public static readonly string[] All = [System, Light, Dark];
}

public sealed class AppSettings
{
    [JsonPropertyName("theme")] public string Theme { get; set; } = ThemeOptions.System;
    [JsonPropertyName("excluded_folders")] public List<string> ExcludedFolders { get; set; } = [];
    [JsonPropertyName("use_only_filename_for_analysis")]
    public bool FilenameOnly { get; set; }
    [JsonPropertyName("metadata_source")] public string MetadataSource { get; set; } = MetadataSources.TvMaze;
}

public sealed class AppFormats
{
    [JsonPropertyName("movie_format")] public string Movie { get; set; } = "{movie_name} ({year})";
    [JsonPropertyName("series_format")]
    public string Series { get; set; } = "S{season_number}E{episode_number} - {episode_title}";
}

public sealed class ApiKeys
{
    [JsonPropertyName("the_movie_db")] public string Tmdb { get; set; } = "";
    [JsonPropertyName("omdb")] public string Omdb { get; set; } = "";
}

public sealed class AppConfiguration
{
    public const string PortableDataDirectoryEnvironmentVariable = "SIMPLER_FILEBOT_PORTABLE_DATA_DIR";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public string DataDirectory { get; }
    public AppSettings Settings { get; private set; }
    public AppFormats Formats { get; private set; }
    public ApiKeys Keys { get; private set; }

    public AppConfiguration(string? dataDirectory = null)
    {
        var launcherDataDirectory = Environment.GetEnvironmentVariable(PortableDataDirectoryEnvironmentVariable);
        if (dataDirectory is not null) DataDirectory = dataDirectory;
        else if (!string.IsNullOrWhiteSpace(launcherDataDirectory))
            DataDirectory = Path.GetFullPath(launcherDataDirectory);
        else if (File.Exists(Path.Combine(AppContext.BaseDirectory, "portable.flag")))
            DataDirectory = Path.Combine(AppContext.BaseDirectory, "data");
        else DataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Simpler FileBot");
        Directory.CreateDirectory(DataDirectory);
        Settings = Read("settings.json", new AppSettings());
        Formats = Read("formats.json", new AppFormats());
        Keys = Read("api_keys.json", new ApiKeys());
        if (!MetadataSources.All.Contains(Settings.MetadataSource)) Settings.MetadataSource = MetadataSources.TvMaze;
        if (!ThemeOptions.All.Contains(Settings.Theme)) Settings.Theme = ThemeOptions.System;
    }

    public void SaveSettings() => Write("settings.json", Settings);
    public void SaveFormats() => Write("formats.json", Formats);
    public void SaveKeys() => Write("api_keys.json", Keys);

    private T Read<T>(string fileName, T fallback)
    {
        var path = Path.Combine(DataDirectory, fileName);
        if (!File.Exists(path)) { Write(fileName, fallback); return fallback; }
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions) ?? fallback; }
        catch (JsonException) { return fallback; }
    }

    private void Write<T>(string fileName, T value)
    {
        Directory.CreateDirectory(DataDirectory);
        var path = Path.Combine(DataDirectory, fileName);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, JsonOptions));
        File.Move(temporary, path, true);
    }
}
