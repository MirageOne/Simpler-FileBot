import sys

from backend.app_paths import application_data_dir
from backend.json_config import JSONConfig


def test_installed_build_uses_user_profile(tmp_path, monkeypatch):
    executable = tmp_path / 'app.exe'
    monkeypatch.setattr(sys, 'frozen', True, raising=False)
    monkeypatch.setattr(sys, 'executable', str(executable))
    profile = tmp_path / 'profile'
    monkeypatch.setattr('backend.app_paths.user_data_dir', lambda **kwargs: str(profile))
    assert application_data_dir() == profile


def test_portable_config_stays_beside_executable(tmp_path, monkeypatch):
    monkeypatch.setattr(sys, 'frozen', True, raising=False)
    monkeypatch.setattr(sys, 'executable', str(tmp_path / 'app.exe'))
    (tmp_path / 'portable.flag').touch()
    config = JSONConfig('settings.json', {'theme': 'Light'})
    assert config.path == tmp_path / 'data' / 'settings.json'
    config.set('theme', 'Dark')
    assert config.get('theme') == 'Dark'


def test_source_checkout_does_not_enable_portable_mode(tmp_path, monkeypatch):
    monkeypatch.setattr(sys, 'frozen', False, raising=False)
    monkeypatch.setattr(sys, 'executable', str(tmp_path / 'python.exe'))
    (tmp_path / 'portable.flag').touch()
    profile = tmp_path / 'profile'
    monkeypatch.setattr('backend.app_paths.user_data_dir', lambda **kwargs: str(profile))
    assert application_data_dir() == profile
