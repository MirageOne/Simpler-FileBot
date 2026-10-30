from unittest.mock import patch

from backend.media_record import MediaRecord
from databases.themoviedb_python_db import TheMovieDBPythonDB


def _simple_hit(title, date):
    return {"title": title, "release_date": date}


def _fake_season_payload(title, episode_number):
    return {"episodes": [{"episode_number": episode_number, "name": title}]}


@patch("databases.themoviedb_python_db.retrieve_the_movie_db_key", return_value="DUMMY_KEY")
@patch("tmdbsimple.Search")
def test_movie_title_happy_path(mock_search_cls, _fake_key):
    # Mock Search.movie() → { "results": [...] }.
    mock_search = mock_search_cls.return_value
    mock_search.movie.side_effect = [
        {"results": [_simple_hit("Oppenheimer", "2023-07-21")]},
        {"results": [_simple_hit("Interstellar", "2014-10-26")]},
    ]

    recs = [MediaRecord("Oppenheimer.2023.mkv"), MediaRecord("Interstellar.mkv")]
    db = TheMovieDBPythonDB(recs, is_tv_series=False)

    assert db.retrieve_media_titles_from_db() == ["Oppenheimer", "Interstellar"]


@patch("databases.themoviedb_python_db.retrieve_the_movie_db_key", return_value="DUMMY_KEY")
@patch("tmdbsimple.TV_Seasons")
@patch("tmdbsimple.Search")
def test_tv_episode_lookup_successful(mock_search_cls, mock_tv_episodes_cls, _fake_key):
    # Mock Search.tv() to give a single series hit.
    mock_search = mock_search_cls.return_value
    mock_search.tv.return_value = {
        "results": [{"id": 42, "first_air_date": "2010-04-17"}]
    }

    # Mock TV_Episodes(...).info() → {"name": ...}
    mock_tv_episodes_cls.return_value.info.return_value = _fake_season_payload("Winter Is Coming", 1)

    rec = MediaRecord("Game.of.Thrones.2011.S01E01.mkv")
    db = TheMovieDBPythonDB([rec], is_tv_series=True)

    assert db.retrieve_media_titles_from_db() == ["Winter Is Coming"]


@patch('tmdbsimple.Search')
def test_no_movie_results_returns_missing_metadata_and_reuses_search(search):
    search.return_value.movie.return_value = {'results': []}
    record = MediaRecord('Unknown Movie.mkv')
    record.year = None
    db = TheMovieDBPythonDB([record])
    assert db.retrieve_media_titles_from_db() == [None]
    assert db.retrieve_media_years_from_db() == [None]
    search.return_value.movie.assert_called_once()


@patch('tmdbsimple.Search')
def test_blank_series_date_returns_none(search):
    search.return_value.tv.return_value = {'results': [{'first_air_date': ''}]}
    record = MediaRecord('Show.S01E01.mkv')
    record.year = None
    assert TheMovieDBPythonDB([record], True).retrieve_media_years_from_db() == [None]


@patch('tmdbsimple.Search')
def test_changed_api_key_is_used_by_next_match(search, monkeypatch):
    import tmdbsimple as tmdb
    import databases.themoviedb_python_db as module
    monkeypatch.setattr(tmdb, 'API_KEY', 'old-key')
    monkeypatch.setattr(module, 'retrieve_the_movie_db_key', lambda: 'new-key')
    observed = []

    def movie(**kwargs):
        observed.append(tmdb.API_KEY)
        return {'results': []}

    search.return_value.movie.side_effect = movie
    TheMovieDBPythonDB([MediaRecord('Unknown.mkv')]).retrieve_media_titles_from_db()
    assert observed == ['new-key']
