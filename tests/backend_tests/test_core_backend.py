import os

import pytest

from backend.core_backend import (match_titles_using_db_and_format, get_invalid_file_names_and_fixes,
                                  perform_file_renaming, create_formatted_title)
from backend.media_record import MediaRecord
from databases.file_name_match_db import FileNameMatchDB


def test_match_titles_using_db_and_format_using_episodes_list_success():
    media_records = [MediaRecord("The.West.Wing.S01E01.Pilot.mkv"), MediaRecord("The.West.Wing.S01E08.Enemies.mkv")]
    database = FileNameMatchDB(media_records, True)

    matched_titles = match_titles_using_db_and_format(database)

    assert matched_titles[0] == "S01E01 - Pilot.mkv"
    assert matched_titles[1] == "S01E08 - Enemies.mkv"


def test_match_titles_using_db_and_format_using_movies_list_success():
    media_records = [MediaRecord("The Lion King (1994).mkv"), MediaRecord("The Lion King (2019).mkv")]
    database = FileNameMatchDB(media_records)

    matched_titles = match_titles_using_db_and_format(database)

    assert matched_titles[0] == "The Lion King (1994).mkv"
    assert matched_titles[1] == "The Lion King (2019).mkv"


def test_get_invalid_file_names_and_fixes():
    file_names = ["Good Name.mkv", "Bad? Name 1.mp4", "S01E01 - Is this fine?.mkv", "<Title> - Hi!.mkv"]

    invalid_file_names_and_fixes = get_invalid_file_names_and_fixes(file_names)

    assert invalid_file_names_and_fixes.get("Bad? Name 1.mp4") == "Bad Name 1.mp4"
    assert invalid_file_names_and_fixes.get("S01E01 - Is this fine?.mkv") == "S01E01 - Is this fine.mkv"
    assert invalid_file_names_and_fixes.get("<Title> - Hi!.mkv") == "Title - Hi!.mkv"


def test_file_rename_success():
    old_file_name = "old.txt"
    new_file_name = "new.txt"
    # Set up the temporary files.
    with open(old_file_name, "w", encoding="utf-8") as file:
        file.write("Hello World!")

    assert os.path.exists(old_file_name)
    try:
        perform_file_renaming([old_file_name], [new_file_name])
        assert not os.path.exists(old_file_name)
        with open(new_file_name, "r", encoding="utf-8") as file:
            content = file.read()
        assert content == "Hello World!"
    finally:
        # Clean up temporary files.
        for file_name in (old_file_name, new_file_name):
            try:
                os.remove(file_name)
            except OSError:
                pass


def test_missing_file_raises_oserror():
    missing_file = "does_not_exist.txt"
    target = "whatever.txt"

    try:
        with pytest.raises(OSError):
            perform_file_renaming([missing_file], [target])
    finally:
        # Clean up any test files.
        for file_name in (missing_file, target):
            try:
                os.remove(file_name)
            except OSError:
                pass


def test_invalid_lists_for_renaming_raises_valueerror():
    old_file_list = ["1", "2"]
    new_file_list = ["1", "2", "3"]

    with pytest.raises(ValueError) as error_info:
        perform_file_renaming(old_file_list, new_file_list)

    error_msg = str(error_info)
    assert "2 files" in error_msg and "3 files" in error_msg


def test_create_formatted_series_title_correctly():
    format_template = "{series_name} ({year}) - S{season_number}E{episode_number} - {episode_title}"
    series_context: dict = {
        "series_name": "The Blacklist",
        "year": "2013",
        "season_number": "03",
        "episode_number": "10",
        "episode_title": None
    }

    formatted_title = create_formatted_title(format_template, series_context)

    assert formatted_title == "The Blacklist (2013) - S03E10 - {None}"


def test_create_formatted_movie_title_correctly():
    format_template = "{year} {movie_name} ({year})"
    movie_context: dict = {
        "movie_name": "Warfare",
        "year": "2025"
    }

    formatted_title = create_formatted_title(format_template, movie_context)

    assert formatted_title == "2025 Warfare (2025)"


def test_existing_target_is_never_overwritten(tmp_path):
    source, target = tmp_path / 'source.mkv', tmp_path / 'target.mkv'
    source.write_text('source')
    target.write_text('keep')
    with pytest.raises(FileExistsError):
        perform_file_renaming([str(source)], [str(target)])
    assert source.read_text() == 'source'
    assert target.read_text() == 'keep'


def test_duplicate_targets_rejected_before_any_rename(tmp_path):
    sources = [tmp_path / name for name in ('a', 'b')]
    for source in sources:
        source.write_text(source.name)
    with pytest.raises(ValueError):
        perform_file_renaming(list(map(str, sources)), [str(tmp_path / 'c')] * 2)
    assert all(source.exists() for source in sources)


def test_missing_source_rejected_before_any_rename(tmp_path):
    source = tmp_path / 'source'
    source.write_text('keep')
    with pytest.raises(FileNotFoundError):
        perform_file_renaming([str(source), str(tmp_path / 'missing')],
                              [str(tmp_path / 'new'), str(tmp_path / 'other')])
    assert source.read_text() == 'keep'


def test_partial_failure_records_only_completed_renames(tmp_path, monkeypatch):
    import backend.core_backend as backend
    sources = [tmp_path / name for name in ('a', 'b')]
    targets = [tmp_path / name for name in ('c', 'd')]
    for source in sources:
        source.write_text(source.name)
    real_rename = backend._rename_without_overwrite

    def fail_second(source, target):
        if source == str(sources[1]):
            raise PermissionError('locked')
        real_rename(source, target)

    monkeypatch.setattr(backend, '_rename_without_overwrite', fail_second)
    completed = []
    with pytest.raises(PermissionError):
        perform_file_renaming(list(map(str, sources)), list(map(str, targets)), completed)
    assert completed == [(str(targets[0]), str(sources[0]))]
    assert targets[0].read_text() == 'a'
    assert sources[1].read_text() == 'b'


def test_target_created_after_validation_is_preserved(tmp_path, monkeypatch):
    import backend.core_backend as backend
    source, target = tmp_path / 'a', tmp_path / 'b'
    source.write_text('source')
    real_rename = backend._rename_without_overwrite

    def race(source, target):
        from pathlib import Path
        Path(target).write_text('racing writer')
        real_rename(source, target)

    monkeypatch.setattr(backend, '_rename_without_overwrite', race)
    with pytest.raises(FileExistsError):
        perform_file_renaming([str(source)], [str(target)])
    assert source.read_text() == 'source'
    assert target.read_text() == 'racing writer'


def test_missing_episode_is_marked_in_output():
    record = MediaRecord('Show.S01E01.mkv')
    record.metadata.pop('episode', None)
    titles = match_titles_using_db_and_format(FileNameMatchDB([record], True))
    assert 'E{None}' in titles[0]


def test_fallback_rename_preserves_existing_destination(tmp_path, monkeypatch):
    import backend.core_backend as backend
    if os.name == 'nt':
        pytest.skip('POSIX fallback')
    monkeypatch.setattr(backend.sys, 'platform', 'darwin')
    source, target = tmp_path / 'a', tmp_path / 'b'
    source.write_text('source')
    target.write_text('keep')
    with pytest.raises(FileExistsError):
        backend._rename_without_overwrite(str(source), str(target))
    assert source.read_text() == 'source'
    assert target.read_text() == 'keep'


def test_fallback_removes_new_link_if_source_cannot_be_removed(tmp_path, monkeypatch):
    import backend.core_backend as backend
    if os.name == 'nt':
        pytest.skip('POSIX fallback')
    monkeypatch.setattr(backend.sys, 'platform', 'darwin')
    source, target = tmp_path / 'a', tmp_path / 'b'
    source.write_text('source')
    real_unlink = os.unlink

    def fail_source(path, *args, **kwargs):
        if path == str(source):
            raise PermissionError('locked')
        real_unlink(path, *args, **kwargs)

    monkeypatch.setattr(backend.os, 'unlink', fail_source)
    with pytest.raises(PermissionError):
        backend._rename_without_overwrite(str(source), str(target))
    assert source.read_text() == 'source'
    assert not target.exists()


@pytest.mark.skipif(os.name != 'nt', reason='Windows case-insensitive rename')
def test_windows_case_only_rename_can_be_undone(tmp_path):
    source, target = tmp_path / 'movie.mkv', tmp_path / 'Movie.mkv'
    source.write_text('keep')
    completed = []
    perform_file_renaming([str(source)], [str(target)], completed)
    assert [path.name for path in tmp_path.iterdir()] == ['Movie.mkv']
    assert completed == [(str(target), str(source))]
    perform_file_renaming([str(target)], [str(source)])
    assert [path.name for path in tmp_path.iterdir()] == ['movie.mkv']


@pytest.mark.skipif(os.name != 'nt', reason='Windows exclusive file lock')
def test_windows_locked_file_raises_and_has_no_undo_entry(tmp_path):
    import ctypes
    from ctypes import wintypes
    source, target = tmp_path / 'source.mkv', tmp_path / 'target.mkv'
    source.write_text('keep')
    kernel = ctypes.WinDLL('kernel32', use_last_error=True)
    kernel.CreateFileW.argtypes = [wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD, wintypes.LPVOID,
                                   wintypes.DWORD, wintypes.DWORD, wintypes.HANDLE]
    kernel.CreateFileW.restype = wintypes.HANDLE
    kernel.CloseHandle.argtypes = [wintypes.HANDLE]
    kernel.CloseHandle.restype = wintypes.BOOL
    handle = kernel.CreateFileW(str(source), 0x80000000, 0, None, 3, 0x80, None)
    assert handle != wintypes.HANDLE(-1).value
    completed = []
    try:
        with pytest.raises(PermissionError):
            perform_file_renaming([str(source)], [str(target)], completed)
    finally:
        kernel.CloseHandle(handle)
    assert completed == []
    assert source.read_text() == 'keep'
    assert not target.exists()
