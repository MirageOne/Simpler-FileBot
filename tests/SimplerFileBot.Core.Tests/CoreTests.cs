using System.Net;
using System.Text;
using SimplerFileBot.Core;
using Xunit;

namespace SimplerFileBot.Core.Tests;

public sealed class CoreTests
{
    [Theory]
    [InlineData("Show.Name.S02E04.Pilot.mkv", MediaKind.Episode, "Show Name", 2, 4)]
    [InlineData("Show.Name.2x04.Pilot.mkv", MediaKind.Episode, "Show Name", 2, 4)]
    [InlineData("Movie.Name.2021.1080p.mkv", MediaKind.Movie, "Movie Name", null, null)]
    public void ParsesCommonMediaNames(string name, MediaKind kind, string title, int? season, int? episode)
    {
        var item = MediaParser.Parse(Path.Combine(Path.GetTempPath(), name), new AppSettings { FilenameOnly = true });
        Assert.Equal(kind, item.Kind);
        Assert.Equal(title, item.Title);
        Assert.Equal(season, item.Season);
        Assert.Equal(episode, item.Episode);
        if (kind == MediaKind.Movie) Assert.Equal(2021, item.Year);
    }

    [Fact]
    public void MissingMetadataCannotBeRenamedAndCustomFormatIsValidated()
    {
        var item = MediaParser.Parse("Example.S01E02.mkv");
        var name = FormatService.BuildName(item, new MatchResult("Example", null, null), new AppFormats());
        Assert.Contains("{None}", name);
        Assert.NotNull(FormatService.ValidateFileName(name));
        Assert.NotNull(FormatService.ValidateFileName("CON.mkv"));
        Assert.NotNull(FormatService.ValidateFileName("episode: title.mkv"));
        Assert.Throws<ArgumentException>(() => FormatService.Preview("{unknown}", MediaKind.Movie));
        Assert.Equal("Interstellar (2014).mkv", FormatService.Preview("{movie_name} ({year})", MediaKind.Movie));
    }

    [Fact]
    public void CollisionPreventsEntireBatchAndUndoRestoresCompletedRename()
    {
        var folder = Path.Combine(Path.GetTempPath(), "filebot-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        try
        {
            var one = Path.Combine(folder, "one.mkv");
            var two = Path.Combine(folder, "two.mkv");
            File.WriteAllText(one, "one");
            File.WriteAllText(two, "two");
            var collision = RenameService.Execute([new RenameEntry(one, "two.mkv")]);
            Assert.NotNull(collision.Error);
            Assert.Empty(collision.Completed);
            Assert.Equal("one", File.ReadAllText(one));
            Assert.Equal("two", File.ReadAllText(two));
            var renamed = RenameService.Execute([new RenameEntry(one, "new.mkv")]);
            Assert.Null(renamed.Error);
            Assert.Single(renamed.Completed);
            Assert.False(File.Exists(one));
            Assert.Null(RenameService.Undo(renamed.Completed).Error);
            Assert.Equal("one", File.ReadAllText(one));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void ExistingPythonSettingsKeepLastMetadataSource()
    {
        var folder = Path.Combine(Path.GetTempPath(), "filebot-settings-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, "settings.json"), "{\"theme\":\"Dark\",\"metadata_source\":\"OMDB\"}");
            var config = new AppConfiguration(folder);
            Assert.Equal("OMDB", config.Settings.MetadataSource);
            config.Settings.MetadataSource = MetadataSources.Tmdb;
            config.SaveSettings();
            Assert.Equal(MetadataSources.Tmdb, new AppConfiguration(folder).Settings.MetadataSource);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public async Task TvMazeMatchesEpisodeAndAbsoluteOrderWithoutApiKey()
    {
        var handler = new FakeHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/search/shows" => "[{\"show\":{\"id\":7,\"name\":\"Example\",\"premiered\":\"2019-01-01\"}}]",
            "/shows/7/episodes" => "[{\"season\":1,\"number\":1,\"name\":\"Pilot\"},{\"season\":2,\"number\":1,\"name\":\"Second\"}]",
            _ => throw new InvalidOperationException(request.RequestUri.ToString())
        });
        var service = new MetadataService(new HttpClient(handler));
        var item = MediaParser.Parse("Example.S01E02.mkv");
        var result = await service.MatchAsync([item], MetadataSources.TvMaze, new ApiKeys(), true);
        Assert.Equal("Second", result[0].EpisodeTitle);
        Assert.Equal(2019, result[0].Year);
    }

    [Fact]
    public async Task TmdbChoosesMovieReleaseNearParsedYear()
    {
        var handler = new FakeHandler(_ => "{\"results\":[{\"title\":\"Wrong\",\"release_date\":\"1980-01-01\"},{\"title\":\"Correct\",\"release_date\":\"2020-01-01\"}]}");
        var service = new MetadataService(new HttpClient(handler));
        var item = MediaParser.Parse("Example.2020.mkv");
        var result = await service.MatchAsync([item], MetadataSources.Tmdb, new ApiKeys { Tmdb = "key" }, false);
        Assert.Equal("Correct", result[0].Title);
        Assert.Equal(2020, result[0].Year);
    }

    [Fact]
    public async Task OmdbMatchesSeasonEpisode()
    {
        var handler = new FakeHandler(request => request.RequestUri!.Query.Contains("Season=2", StringComparison.Ordinal)
            ? "{\"Episodes\":[{\"Episode\":\"4\",\"Title\":\"The Fourth\"}]}"
            : "{\"Title\":\"Example\",\"Year\":\"2018–2021\",\"totalSeasons\":\"2\"}");
        var service = new MetadataService(new HttpClient(handler));
        var item = MediaParser.Parse("Example.S02E04.mkv");
        var result = await service.MatchAsync([item], MetadataSources.Omdb, new ApiKeys { Omdb = "key" }, false);
        Assert.Equal("The Fourth", result[0].EpisodeTitle);
        Assert.Equal(2018, result[0].Year);
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, string> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(response(request), Encoding.UTF8, "application/json") });
    }
}
