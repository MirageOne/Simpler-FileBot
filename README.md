# Simpler FileBot

Simpler FileBot is a native Windows media renamer built with C#, .NET 10, WinUI 3, and the Windows App SDK. It can match movie and TV episode names with TVMaze, TheMovieDB, OMDB, or local filename metadata, then show a reviewable rename preview. The app does not require Python.

## Use

Add files, drag them into the window, or scan a folder. Choose a metadata source and select **Match metadata**. Edit any proposed filename before selecting **Rename checked**. Rows with missing metadata, invalid Windows names, or destination collisions must be resolved first. **Undo last rename** restores completed changes, including after a partial failure.

The source you select is remembered for the next launch. TVMaze supports TV episodes; TheMovieDB and OMDB support movies and episodes and require API keys on the **Metadata** page. **Formats** controls the movie and episode filename templates. **Settings** follows the Windows theme by default, with light and dark overrides, and also controls folder exclusions and filename-only analysis.

The default templates are `{movie_name} ({year})` and `S{season_number}E{episode_number} - {episode_title}`. The original file extension is retained. Episode matching uses one show at a time and recognizes common `S01E02`, `1x02`, and season-folder `E02` names. Multiple episodes in one filename use the first episode number. The native parser does not use Python's `guessit` library, so review unusual release names before renaming.

## Build

See [Windows build instructions](docs/windows-build.md) for the Windows prerequisites and exact commands to run the app, build a single-file portable EXE, and build an Inno Setup installer. Open `SimplerFileBot.sln` in Visual Studio, or use the projects in `src/SimplerFileBot.Core` and `src/SimplerFileBot.WinUI`; core tests are in `tests/SimplerFileBot.Core.Tests`.

The portable EXE keeps settings in `data` beside itself. It unpacks the native runtime to a temporary folder while the app runs and removes that folder when the app closes. The installer stores settings under `%LOCALAPPDATA%\Simpler FileBot`. Existing Python edition JSON settings, formats, and API keys in that folder are read by the native app.

## Metadata services

The app is independent of its metadata providers. TVMaze data comes from [TVMaze](https://www.tvmaze.com/); TheMovieDB data comes from [TMDB](https://www.themoviedb.org/) and needs a [TMDB API key](https://www.themoviedb.org/settings/api); OMDB data comes from [OMDb API](https://www.omdbapi.com/) and needs an [OMDb API key](https://www.omdbapi.com/apikey.aspx).

## License

See [LICENSE](LICENSE).
