from types import SimpleNamespace
from unittest.mock import patch, MagicMock

from backend.media_record import MediaRecord
from databases.tvmaze_python_db import get_premiere_year_of_listing, filter_listings_within_one_year_of_target, \
    TVMazePythonDB


def test_get_premiere_year_of_listing_successful():
    # Even though get_premiere_year_of_listing's parameter is (Model | None),
    # we can use SimpleNamespace since it also has a 'premiered' field. This avoids an API call.
    listing = SimpleNamespace(premiered="1999-09-22")

    assert get_premiere_year_of_listing(listing) == 1999


def test_filter_listings_within_one_year_of_target_successful():
    target_year = 2005
    # We can use SimpleNamespace since it also has a 'premiered' field. This avoids an API call.
    listings = [
        SimpleNamespace(premiered="2005-03-26"),
        SimpleNamespace(premiered="2004-04-02"),
        SimpleNamespace(premiered="1963-11-23"),
        SimpleNamespace(premiered=None),
    ]

    filtered = filter_listings_within_one_year_of_target(listings, target_year)

    # Check that the listings are all within a year of the target year.
    assert all(abs(get_premiere_year_of_listing(listing) - target_year) <= 1 for listing in filtered)
    assert len(filtered) <= len(listings)


@patch("databases.tvmaze_python_db.Api")
def test_retrieve_media_years_for_series_from_db_successful(mock_api_cls):
    mock_api = mock_api_cls.return_value
    mock_api.search.shows.return_value = [SimpleNamespace(premiered="1999-09-22")]

    db = TVMazePythonDB(
        [
            MediaRecord("The.West.Wing.S01E01.mkv"),
            MediaRecord("The.West.Wing.S07E01.mkv"),
        ],
        True,
    )

    assert db.retrieve_media_years_from_db() == [1999, 1999]


@patch("databases.tvmaze_python_db.Api")
def test_retrieve_media_titles_from_db_successful(mock_api_cls):
    mock_api = mock_api_cls.return_value
    mock_api.search.shows.return_value = [SimpleNamespace(id=42, premiered="1999-09-22")]

    mock_api.show = MagicMock()
    mock_api.show.episodes.return_value = [
        SimpleNamespace(season=2, number=22, name="Two Cathedrals"),
        SimpleNamespace(season=4, number=23, name="Twenty Five"),
    ]

    db = TVMazePythonDB(
        [
            MediaRecord("The.West.Wing.S02E22.mkv"),
            MediaRecord("The.West.Wing.S04E23.mkv"),
        ],
        True,
    )

    assert db.retrieve_media_titles_from_db() == ["Two Cathedrals", "Twenty Five"]


@patch('databases.tvmaze_python_db.Api')
def test_title_and_year_reuse_show_search(api_cls):
    api = api_cls.return_value
    api.search.shows.return_value = [SimpleNamespace(id=42, premiered='1999-09-22')]
    api.show.episodes.return_value = [SimpleNamespace(season=1, number=1, name='Pilot')]
    record = MediaRecord('The.West.Wing.S01E01.mkv')
    record.year = None
    db = TVMazePythonDB([record], True)
    assert db.retrieve_media_titles_from_db() == ['Pilot']
    assert db.retrieve_media_years_from_db() == [1999]
    api.search.shows.assert_called_once()
