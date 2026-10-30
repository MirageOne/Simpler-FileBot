using System.Text.RegularExpressions;

namespace SimplerFileBot.Core;

public enum MediaKind { Movie, Episode }

public sealed class MediaItem
{
    // WinUI's generated binding metadata needs a public parameterless constructor.
    public string SourcePath { get; set; } = "";
    public string Title { get; set; } = "";
    public MediaKind Kind { get; init; }
    public int? Year { get; set; }
    public int? Season { get; init; }
    public int? Episode { get; init; }
    public string? EpisodeTitle { get; init; }
    public string Extension => Path.GetExtension(SourcePath);
}

public static partial class MediaParser
{
    private static readonly Regex EpisodePattern = new(@"\bS(?<season>\d{1,3})[ ._-]*E(?<episode>\d{1,4})(?:[ ._-]*E?\d{1,4})?\b|\b(?<season>\d{1,3})x(?<episode>\d{1,4})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex YearPattern = new(@"(?<!\d)(?:19\d{2}|20\d{2})(?!\d)", RegexOptions.Compiled);
    private static readonly Regex SeasonFolderPattern = new(@"\bSeason[ ._-]*(?<season>\d{1,3})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex EpisodeOnlyPattern = new(@"\bE(?<episode>\d{1,4})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex NoisePattern = new(@"\b(?:2160p|1080p|720p|480p|BluRay|WEB[ ._-]?DL|WEBRip|DVDRip|x264|x265|H[ ._-]?264|H[ ._-]?265|HEVC|AAC|DTS|HDR|REPACK|PROPER)\b.*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SeparatorPattern = new(@"[._]+|\s+-\s+", RegexOptions.Compiled);

    public static MediaItem Parse(string path, AppSettings? settings = null)
    {
        var fileName = Path.GetFileNameWithoutExtension(path);
        var folder = Path.GetDirectoryName(path) ?? "";
        var useFolders = !(settings?.FilenameOnly ?? false) && !IsExcluded(folder, settings?.ExcludedFolders);
        var episode = EpisodePattern.Match(fileName);
        var season = 0;
        if (!episode.Success && useFolders)
        {
            var seasonFolder = SeasonFolderPattern.Match(folder);
            var episodeOnly = EpisodeOnlyPattern.Match(fileName);
            if (seasonFolder.Success && episodeOnly.Success)
            {
                episode = episodeOnly;
                season = int.Parse(seasonFolder.Groups["season"].Value);
            }
        }

        if (episode.Success)
        {
            if (episode.Groups["season"].Success) season = int.Parse(episode.Groups["season"].Value);
            var number = int.Parse(episode.Groups["episode"].Value);
            var before = Clean(fileName[..episode.Index]);
            var after = Clean(fileName[(episode.Index + episode.Length)..]);
            var parentTitle = useFolders ? Clean(Path.GetFileName(Path.GetDirectoryName(folder) ?? "")) : "";
            var folderTitle = useFolders ? Clean(Path.GetFileName(folder)) : "";
            var title = before.Length > 0 ? before : (SeasonFolderPattern.IsMatch(folderTitle) ? parentTitle : folderTitle);
            return new MediaItem
            {
                SourcePath = path, Title = title, Kind = MediaKind.Episode, Season = season == 0 ? 1 : season,
                Episode = number, EpisodeTitle = after.Length > 0 ? after : null,
                Year = FindYear(fileName) ?? (useFolders ? FindYear(folder) : null)
            };
        }

        var year = YearPattern.Match(fileName);
        var movieTitle = Clean(year.Success ? fileName[..year.Index] : fileName);
        if (movieTitle.Length == 0 && useFolders) movieTitle = Clean(Path.GetFileName(folder));
        return new MediaItem { SourcePath = path, Title = movieTitle, Kind = MediaKind.Movie,
            Year = FindYear(fileName) ?? (useFolders ? FindYear(folder) : null) };
    }

    private static bool IsExcluded(string folder, IEnumerable<string>? exclusions) => exclusions?.Any(excluded =>
        folder.Equals(excluded, StringComparison.OrdinalIgnoreCase) ||
        folder.StartsWith(Path.TrimEndingDirectorySeparator(excluded) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) ?? false;

    private static int? FindYear(string value) => YearPattern.Match(value) is { Success: true } match ? int.Parse(match.Value) : null;

    private static string Clean(string value)
    {
        value = NoisePattern.Replace(value, "");
        value = YearPattern.Replace(value, "");
        value = SeparatorPattern.Replace(value, " ");
        return value.Trim(' ', '-', '.', '_', '(', ')', '[', ']');
    }
}
