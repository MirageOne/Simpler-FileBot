import pytest
from PySide6.QtCore import Qt

from backend import settings_backend
from backend.json_config import JSONConfig
from backend.settings_backend import (retrieve_theme_from_settings, save_new_theme_to_settings,
                                      retrieve_excluded_folders, add_excluded_folder, remove_excluded_folder,
                                      retrieve_filename_analysis_only_flag, set_filename_analysis_only_flag,
                                      retrieve_metadata_source, save_metadata_source)


# pylint: disable=unused-argument, redefined-outer-name
@pytest.fixture
def redirect_settings_file_path_to_temp_file(tmp_path, monkeypatch):
    """Redirect 'settings_json_config' to a test one, then put it back afterward."""
    fake_path = tmp_path / "settings.json"

    test_settings_config = JSONConfig(
        "settings.json",
        {
            "theme": "Dark",
            "excluded_folders": []
        }
    )
    test_settings_config.path = fake_path
    test_settings_config.delete_and_recreate_file()

    monkeypatch.setattr(settings_backend, "_settings_json_config", test_settings_config)

    yield fake_path


# pylint: disable=condition-evals-to-constant
def test_retrieve_theme_from_settings_successful(redirect_settings_file_path_to_temp_file):
    retrieved_theme = retrieve_theme_from_settings()

    assert retrieved_theme == "Dark"


def test_save_new_theme_to_settings_successful(redirect_settings_file_path_to_temp_file):
    save_new_theme_to_settings(Qt.ColorScheme.Unknown)

    assert retrieve_theme_from_settings() == "Unknown"


def test_retrieve_excluded_folders_successfully(redirect_settings_file_path_to_temp_file):
    assert len(retrieve_excluded_folders()) == 0


def test_add_excluded_folder_saves_successfully(redirect_settings_file_path_to_temp_file):
    test_folder = "C:/Exclude This Folder/"

    add_excluded_folder(test_folder)

    assert retrieve_excluded_folders()[0] == test_folder


def test_retrieve_filename_analysis_only_flag(redirect_settings_file_path_to_temp_file):
    assert not retrieve_filename_analysis_only_flag()


def test_set_filename_analysis_only_flag(redirect_settings_file_path_to_temp_file):
    assert not retrieve_filename_analysis_only_flag()

    set_filename_analysis_only_flag(True)

    assert retrieve_filename_analysis_only_flag()


def test_remove_excluded_folder_deletes_successfully(redirect_settings_file_path_to_temp_file):
    test_folder = "C:/Remove This Folder Once Added/"

    add_excluded_folder(test_folder)

    assert retrieve_excluded_folders()[0] == test_folder

    remove_excluded_folder(test_folder)

    assert len(retrieve_excluded_folders()) == 0


def test_metadata_source_is_saved_and_restored(redirect_settings_file_path_to_temp_file):
    assert retrieve_metadata_source() == "TVMaze"  # Older settings files have no source key.
    save_metadata_source("Local metadata")
    assert retrieve_metadata_source() == "Local metadata"


def test_invalid_metadata_source_falls_back_safely(redirect_settings_file_path_to_temp_file):
    settings_backend.ensure().set("metadata_source", "Retired provider")
    assert retrieve_metadata_source() == "TVMaze"
    with pytest.raises(ValueError):
        save_metadata_source("Retired provider")
