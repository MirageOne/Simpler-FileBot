using System.Net.Http.Json;
using System.Text.Json;

namespace SimplerFileBot.Core;

public sealed record MatchResult(string? Title, int? Year, string? EpisodeTitle);

public sealed class MetadataService(HttpClient client)
{
    public async Task<IReadOnlyList<MatchResult>> MatchAsync(
        IReadOnlyList<MediaItem> items, string source, ApiKeys keys, bool absoluteOrder, CancellationToken cancellationToken = default)
    {
        if (items.Count == 0) return [];
        if (items.Any(item => string.IsNullOrWhiteSpace(item.Title)))
            throw new ArgumentException("A title could not be detected. Enter a title override before matching.");
        if (items.Any(item => item.Kind != items[0].Kind)) throw new ArgumentException("Match movies and episodes in separate batches.");
        if (source == MetadataSources.Local) return items.Select(i => new MatchResult(i.Title, i.Year, i.EpisodeTitle)).ToList();
        if (source == MetadataSources.TvMaze)
        {
            if (items[0].Kind != MediaKind.Episode) throw new ArgumentException("TVMaze only supports TV episodes. Choose another source for movies.");
            return await MatchSeriesGroupsAsync(items,
                (series, token) => MatchTvMazeAsync(series, absoluteOrder, token), cancellationToken);
        }
        if (source == MetadataSources.Tmdb)
        {
            if (string.IsNullOrWhiteSpace(keys.Tmdb)) throw new ArgumentException("Add a TheMovieDB API key on the Metadata page.");
            return items[0].Kind == MediaKind.Movie
                ? await MatchMoviesAsync(items, (item, token) => TmdbMovieAsync(item, keys.Tmdb, token), cancellationToken)
                : await MatchSeriesGroupsAsync(items,
                    (series, token) => MatchTmdbSeriesAsync(series, keys.Tmdb, absoluteOrder, token), cancellationToken);
        }
        if (source == MetadataSources.Omdb)
        {
            if (string.IsNullOrWhiteSpace(keys.Omdb)) throw new ArgumentException("Add an OMDB API key on the Metadata page.");
            return items[0].Kind == MediaKind.Movie
                ? await MatchMoviesAsync(items, (item, token) => OmdbMovieAsync(item, keys.Omdb, token), cancellationToken)
                : await MatchSeriesGroupsAsync(items,
                    (series, token) => MatchOmdbSeriesAsync(series, keys.Omdb, absoluteOrder, token), cancellationToken);
        }
        throw new ArgumentException($"Unknown metadata source: {source}");
    }

    private static async Task<IReadOnlyList<MatchResult>> MatchMoviesAsync(IReadOnlyList<MediaItem> items,
        Func<MediaItem, CancellationToken, Task<MatchResult>> match, CancellationToken token)
    {
        var result = new List<MatchResult>(items.Count);
        var cache = new Dictionary<(string Title, int? Year), MatchResult>();
        foreach (var item in items)
        {
            var key = (item.Title.ToUpperInvariant(), item.Year);
            if (!cache.TryGetValue(key, out var matched)) cache[key] = matched = await match(item, token);
            result.Add(matched);
        }
        return result;
    }

    private static async Task<IReadOnlyList<MatchResult>> MatchSeriesGroupsAsync(IReadOnlyList<MediaItem> items,
        Func<IReadOnlyList<MediaItem>, CancellationToken, Task<IReadOnlyList<MatchResult>>> match, CancellationToken token)
    {
        var results = new MatchResult[items.Count];
        var groups = items.Select((item, index) => (Item: item, Index: index))
            .GroupBy(entry => (Title: entry.Item.Title.Trim().ToUpperInvariant(), entry.Item.Year));
        foreach (var group in groups)
        {
            token.ThrowIfCancellationRequested();
            var entries = group.ToArray();
            var matches = await match(entries.Select(entry => entry.Item).ToArray(), token);
            for (var i = 0; i < entries.Length; i++) results[entries[i].Index] = matches[i];
        }
        return results;
    }

    private async Task<MatchResult> TmdbMovieAsync(MediaItem item, string key, CancellationToken token)
    {
        var response = await GetAsync($"https://api.themoviedb.org/3/search/movie?api_key={Uri.EscapeDataString(key)}&query={Uri.EscapeDataString(item.Title)}", token);
        var best = BestByYear(Array(response, "results"), item.Year, "release_date");
        return best is null ? new MatchResult(null, item.Year, null)
            : new MatchResult(String(best.Value, "title"), item.Year ?? Year(String(best.Value, "release_date")), null);
    }

    private async Task<IReadOnlyList<MatchResult>> MatchTmdbSeriesAsync(IReadOnlyList<MediaItem> items, string key, bool absolute, CancellationToken token)
    {
        var first = items[0];
        var search = await GetAsync($"https://api.themoviedb.org/3/search/tv?api_key={Uri.EscapeDataString(key)}&query={Uri.EscapeDataString(first.Title)}", token);
        var best = BestByYear(Array(search, "results"), first.Year, "first_air_date");
        if (best is null) return Missing(items);
        var id = Int(best.Value, "id");
        if (id is null) return Missing(items);
        var year = first.Year ?? Year(String(best.Value, "first_air_date"));
        var show = String(best.Value, "name") ?? first.Title;
        var seasons = items.Select(i => i.Season ?? 1).Distinct().Order().ToList();
        if (absolute)
        {
            var detail = await GetAsync($"https://api.themoviedb.org/3/tv/{id}?api_key={Uri.EscapeDataString(key)}", token);
            seasons = Enumerable.Range(1, Math.Clamp(Int(detail, "number_of_seasons") ?? 1, 1, 100)).ToList();
        }
        var episodes = new Dictionary<(int, int), string?>();
        var absoluteNumber = 1;
        foreach (var season in seasons)
        {
            var detail = await GetAsync($"https://api.themoviedb.org/3/tv/{id}/season/{season}?api_key={Uri.EscapeDataString(key)}", token);
            foreach (var episode in Array(detail, "episodes"))
            {
                var number = Int(episode, "episode_number");
                if (number is null) continue;
                episodes[(season, number.Value)] = String(episode, "name");
                if (absolute) episodes[(1, absoluteNumber++)] = String(episode, "name");
            }
        }
        return items.Select(i => new MatchResult(show, year,
            episodes.GetValueOrDefault((absolute ? 1 : i.Season ?? 1, i.Episode ?? -1)))).ToList();
    }

    private async Task<IReadOnlyList<MatchResult>> MatchTvMazeAsync(IReadOnlyList<MediaItem> items, bool absolute, CancellationToken token)
    {
        var first = items[0];
        var search = await GetAsync($"https://api.tvmaze.com/search/shows?q={Uri.EscapeDataString(first.Title)}", token);
        var shows = search.ValueKind == JsonValueKind.Array ? search.EnumerateArray().Select(e => Property(e, "show")).Where(e => e is not null).Select(e => e!.Value).ToList() : [];
        var best = BestByYear(shows, first.Year, "premiered");
        if (best is null) return Missing(items);
        var id = Int(best.Value, "id");
        if (id is null) return Missing(items);
        var year = first.Year ?? Year(String(best.Value, "premiered"));
        var show = String(best.Value, "name") ?? first.Title;
        var response = await GetAsync($"https://api.tvmaze.com/shows/{id}/episodes", token);
        var episodes = new Dictionary<(int, int), string?>();
        var absoluteNumber = 1;
        if (response.ValueKind == JsonValueKind.Array)
            foreach (var episode in response.EnumerateArray())
            {
                var season = Int(episode, "season"); var number = Int(episode, "number");
                if (season is null || number is null) continue;
                episodes[(season.Value, number.Value)] = String(episode, "name");
                if (absolute) episodes[(1, absoluteNumber++)] = String(episode, "name");
            }
        return items.Select(i => new MatchResult(show, year,
            episodes.GetValueOrDefault((absolute ? 1 : i.Season ?? 1, i.Episode ?? -1)))).ToList();
    }

    private async Task<MatchResult> OmdbMovieAsync(MediaItem item, string key, CancellationToken token)
    {
        var response = await OmdbAsync(item.Title, item.Year, null, key, token);
        return new MatchResult(String(response, "Title"), item.Year ?? Year(String(response, "Year")), null);
    }

    private async Task<IReadOnlyList<MatchResult>> MatchOmdbSeriesAsync(IReadOnlyList<MediaItem> items, string key, bool absolute, CancellationToken token)
    {
        var first = items[0];
        var show = await OmdbAsync(first.Title, first.Year, null, key, token);
        var year = first.Year ?? Year(String(show, "Year"));
        var title = String(show, "Title") ?? first.Title;
        var seasons = absolute
            ? Enumerable.Range(1, Math.Clamp(Int(show, "totalSeasons") ?? 1, 1, 100))
            : items.Select(i => i.Season ?? 1).Distinct().Order();
        var episodes = new Dictionary<(int, int), string?>();
        var absoluteNumber = 1;
        foreach (var season in seasons)
        {
            var detail = await OmdbAsync(first.Title, first.Year, season, key, token);
            foreach (var episode in Array(detail, "Episodes"))
            {
                var number = Int(episode, "Episode");
                if (number is null) continue;
                episodes[(season, number.Value)] = String(episode, "Title");
                if (absolute) episodes[(1, absoluteNumber++)] = String(episode, "Title");
            }
        }
        return items.Select(i => new MatchResult(title, year,
            episodes.GetValueOrDefault((absolute ? 1 : i.Season ?? 1, i.Episode ?? -1)))).ToList();
    }

    private Task<JsonElement> OmdbAsync(string title, int? year, int? season, string key, CancellationToken token)
    {
        var url = $"https://www.omdbapi.com/?apikey={Uri.EscapeDataString(key)}&t={Uri.EscapeDataString(title)}";
        if (year is not null) url += $"&y={year}";
        if (season is not null) url += $"&Season={season}";
        return GetAsync(url, token);
    }

    private async Task<JsonElement> GetAsync(string url, CancellationToken token)
    {
        var result = await client.GetFromJsonAsync<JsonElement>(url, token);
        if (String(result, "Response") == "False" && String(result, "Error") is { } error &&
            (error.Contains("key", StringComparison.OrdinalIgnoreCase) || error.Contains("limit", StringComparison.OrdinalIgnoreCase)))
            throw new HttpRequestException(error);
        return result;
    }

    private static IReadOnlyList<MatchResult> Missing(IReadOnlyList<MediaItem> items) =>
        items.Select(i => new MatchResult(i.Kind == MediaKind.Episode ? i.Title : null, i.Year, null)).ToList();

    private static JsonElement? BestByYear(IEnumerable<JsonElement> results, int? year, string dateField)
    {
        var list = results.ToList();
        if (year is null) return list.Count > 0 ? list[0] : null;
        return list.FirstOrDefault(e => Year(String(e, dateField)) is int found && Math.Abs(found - year.Value) <= 1) is { ValueKind: not JsonValueKind.Undefined } near
            ? near : (list.Count > 0 ? list[0] : null);
    }

    private static IEnumerable<JsonElement> Array(JsonElement parent, string name) =>
        Property(parent, name) is { ValueKind: JsonValueKind.Array } array ? array.EnumerateArray() : [];
    private static JsonElement? Property(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value) ? value : null;
    private static string? String(JsonElement parent, string name) =>
        Property(parent, name) is { } value && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static int? Int(JsonElement parent, string name)
    {
        var value = Property(parent, name);
        if (value is null) return null;
        if (value.Value.ValueKind == JsonValueKind.Number && value.Value.TryGetInt32(out var number)) return number;
        return value.Value.ValueKind == JsonValueKind.String && int.TryParse(value.Value.GetString(), out number) ? number : null;
    }
    private static int? Year(string? text) => text is { Length: >= 4 } && int.TryParse(text[..4], out var year) ? year : null;
}
