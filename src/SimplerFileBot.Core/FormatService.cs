using System.Text;

namespace SimplerFileBot.Core;

public static class FormatService
{
    private static readonly HashSet<string> MovieFields = ["movie_name", "year"];
    private static readonly HashSet<string> SeriesFields = ["series_name", "year", "season_number", "episode_number", "episode_title"];
    private static readonly char[] InvalidWindowsFileChars = "<>:\"/\\|?*".ToCharArray();

    public static string Preview(string format, MediaKind kind)
    {
        var example = kind == MediaKind.Movie
            ? new Dictionary<string, string?> { ["movie_name"] = "Interstellar", ["year"] = "2014" }
            : new Dictionary<string, string?> { ["series_name"] = "The West Wing", ["year"] = "1999", ["season_number"] = "02", ["episode_number"] = "22", ["episode_title"] = "Two Cathedrals" };
        var result = Expand(format, example, kind);
        var error = ValidateFileName(result + ".mkv");
        if (error is not null) throw new ArgumentException(error, nameof(format));
        return result + ".mkv";
    }

    public static string BuildName(MediaItem item, MatchResult match, AppFormats formats)
    {
        var values = new Dictionary<string, string?>
        {
            ["movie_name"] = match.Title, ["series_name"] = match.Title,
            ["year"] = match.Year?.ToString(),
            ["season_number"] = item.Season?.ToString("D2"),
            ["episode_number"] = item.Episode?.ToString("D2"),
            ["episode_title"] = match.EpisodeTitle
        };
        return Expand(item.Kind == MediaKind.Movie ? formats.Movie : formats.Series, values, item.Kind) + item.Extension;
    }

    public static string? ValidateFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or "..") return "The filename is empty.";
        if (name.IndexOfAny(InvalidWindowsFileChars) >= 0 || name.Any(c => c < 32)) return "The filename contains a Windows reserved character.";
        if (name.EndsWith('.') || name.EndsWith(' ')) return "Windows filenames cannot end in a space or period.";
        var stem = Path.GetFileNameWithoutExtension(name).TrimEnd(' ', '.');
        if (new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(stem, StringComparer.OrdinalIgnoreCase))
            return "The filename uses a reserved Windows device name.";
        if (name.Contains("{None}", StringComparison.Ordinal)) return "Metadata is missing. Edit the name or change the match.";
        return null;
    }

    private static string Expand(string format, IReadOnlyDictionary<string, string?> values, MediaKind kind)
    {
        var fields = kind == MediaKind.Movie ? MovieFields : SeriesFields;
        var output = new StringBuilder();
        for (var i = 0; i < format.Length; i++)
        {
            if (format[i] == '{')
            {
                if (i + 1 < format.Length && format[i + 1] == '{') { output.Append('{'); i++; continue; }
                var end = format.IndexOf('}', i + 1);
                if (end < 0) throw new ArgumentException("A format placeholder is missing its closing brace.");
                var field = format[(i + 1)..end];
                if (!fields.Contains(field)) throw new ArgumentException($"Unknown placeholder {{{field}}}.");
                output.Append(values.TryGetValue(field, out var value) && !string.IsNullOrWhiteSpace(value) ? value : "{None}");
                i = end;
            }
            else if (format[i] == '}')
            {
                if (i + 1 < format.Length && format[i + 1] == '}') { output.Append('}'); i++; }
                else throw new ArgumentException("A format has an unmatched closing brace.");
            }
            else output.Append(format[i]);
        }
        if (output.Length == 0 || string.IsNullOrWhiteSpace(output.ToString())) throw new ArgumentException("The format must produce a filename.");
        return output.ToString();
    }
}
