from PySide6.QtCore import Qt

import backend.core_backend as backend
from backend.media_record import MediaRecord
from pages.core.core_renamer_widget import CoreRenamerWidget


def prepare(widget, sources, targets):
    for source in sources:
        source.write_text(source.name)
    widget.table.add_records([MediaRecord(str(source)) for source in sources])
    widget.apply_matches([target.name for target in targets])


def test_partial_rename_can_be_undone(qtbot, tmp_path, monkeypatch):
    widget = CoreRenamerWidget()
    qtbot.addWidget(widget)
    sources = [tmp_path / name for name in ('First.2000.mkv', 'Second.2001.mkv')]
    targets = [tmp_path / name for name in ('renamed-first.mkv', 'renamed-second.mkv')]
    prepare(widget, sources, targets)
    real_rename = backend._rename_without_overwrite

    def fail_second(source, target):
        if source == str(sources[1]):
            raise PermissionError('locked')
        real_rename(source, target)

    monkeypatch.setattr(backend, '_rename_without_overwrite', fail_second)
    widget.rename_files_if_allowed()
    assert widget.last_renames == [(str(targets[0]), str(sources[0]))]
    assert widget.undo_button.isEnabled()
    assert widget.table.rowCount() == 2
    assert widget.table.record(0).full_file_path == str(targets[0])
    assert '1 of 2' in widget.info_text.text()
    widget.undo_last_rename_operation()
    assert all(source.exists() for source in sources)
    assert not any(target.exists() for target in targets)
    assert widget.table.record(0).full_file_path == str(sources[0])
    assert widget.last_renames == []
    assert not widget.undo_button.isEnabled()


def test_failed_undo_retains_history_for_retry(qtbot, tmp_path):
    widget = CoreRenamerWidget()
    qtbot.addWidget(widget)
    original, renamed = tmp_path / 'original', tmp_path / 'renamed'
    renamed.write_text('keep')
    widget.last_renames = [(str(renamed), str(original))]
    original.write_text('collision')
    widget.undo_last_rename_operation()
    assert widget.last_renames == [(str(renamed), str(original))]
    assert widget.undo_button.isEnabled()
    assert original.read_text() == 'collision'
    original.unlink()
    widget.undo_last_rename_operation()
    assert original.read_text() == 'keep'
    assert widget.last_renames == []


def test_empty_undo_is_safe(qtbot):
    widget = CoreRenamerWidget()
    qtbot.addWidget(widget)
    widget.undo_last_rename_operation()
    assert not widget.undo_button.isEnabled()


def test_review_file_is_skipped_until_edited(qtbot, tmp_path):
    widget = CoreRenamerWidget()
    qtbot.addWidget(widget)
    sources = [tmp_path / name for name in ('Show.S01E01.mkv', 'Show.S01E02.mkv')]
    prepare(widget, sources, [tmp_path / 'S01E01 - Pilot.mkv', tmp_path / 'S01E02 - {None}.mkv'])
    assert widget.table.item(1, 3).text() == 'Review'
    assert widget.table.item(1, 0).checkState() == Qt.CheckState.Unchecked
    assert widget.selected_ready_rows() == [0]
    widget.rename_files_if_allowed()
    assert (tmp_path / 'S01E01 - Pilot.mkv').exists()
    assert sources[1].exists()
    widget.table.item(1, 2).setText('S01E02 - Second Episode.mkv')
    assert widget.selected_ready_rows() == [1]
    widget.rename_files_if_allowed()
    assert not sources[1].exists()
    assert (tmp_path / 'S01E02 - Second Episode.mkv').exists()


def test_existing_and_duplicate_destinations_require_review(qtbot, tmp_path):
    widget = CoreRenamerWidget()
    qtbot.addWidget(widget)
    source = tmp_path / 'Movie.mkv'
    target = tmp_path / 'existing.mkv'
    target.write_text('keep')
    prepare(widget, [source], [target])
    assert widget.table.item(0, 3).text() == 'Review'
    assert not widget.rename_button.isEnabled()
    assert target.read_text() == 'keep'
    other = tmp_path / 'Other.mkv'
    other.touch()
    widget.table.add_records([MediaRecord(str(other))])
    widget.apply_matches(['same.mkv', 'same.mkv'])
    assert widget.selected_ready_rows() == []
    widget.table.item(1, 0).setCheckState(Qt.CheckState.Unchecked)
    assert widget.selected_ready_rows() == [0]


def test_local_inline_matching_uses_real_backend(qtbot, tmp_path, monkeypatch):
    # Keep temporary test-directory names out of GuessIt's filename analysis.
    monkeypatch.setattr('backend.media_record.retrieve_filename_analysis_only_flag', lambda: True)
    widget = CoreRenamerWidget()
    qtbot.addWidget(widget)
    source = tmp_path / 'The.West.Wing.S01E01.Pilot.mkv'
    source.touch()
    widget.table.add_records([MediaRecord(str(source))])
    widget.database_combo.setCurrentText('Local metadata')
    widget.match_files()
    qtbot.waitUntil(lambda: not widget._busy)
    assert widget.table.item(0, 2).text() == 'S01E01 - Pilot.mkv'
    assert widget.rename_button.isEnabled()
    widget.rename_files_if_allowed()
    assert (tmp_path / 'S01E01 - Pilot.mkv').exists()
    widget.undo_last_rename_operation()
    assert source.exists()


def test_import_is_async_and_deduplicates_paths(qtbot, tmp_path):
    widget = CoreRenamerWidget()
    qtbot.addWidget(widget)
    path = tmp_path / 'Movie.mkv'
    path.touch()
    widget.add_files([str(path), str(path)])
    qtbot.waitUntil(lambda: not widget._busy)
    assert widget.table.rowCount() == 1
    assert widget.table._progress is None


def test_clear_and_cancel_preserve_undo_history(qtbot, tmp_path):
    widget = CoreRenamerWidget()
    qtbot.addWidget(widget)
    source, target = tmp_path / 'Movie.mkv', tmp_path / 'Renamed.mkv'
    prepare(widget, [source], [target])
    widget.rename_files_if_allowed()
    history = list(widget.last_renames)
    widget.clear_preview()
    widget.clear_files()
    assert widget.table.rowCount() == 0
    assert widget.last_renames == history
    assert widget.undo_button.isEnabled()


def test_table_import_can_be_cancelled(qtbot, tmp_path, monkeypatch):
    from threading import Event
    from PySide6.QtWidgets import QPushButton
    import backend.file_import_worker as module
    widget = CoreRenamerWidget()
    qtbot.addWidget(widget)
    source = tmp_path / 'Movie.mkv'
    source.touch()
    started, release = Event(), Event()
    real_record = module.MediaRecord

    def slow_record(path):
        started.set()
        if not release.wait(5):
            raise RuntimeError('Parser was not released')
        return real_record(path)

    monkeypatch.setattr(module, 'MediaRecord', slow_record)
    widget.add_files([str(source)])
    try:
        qtbot.waitUntil(started.is_set)
        cancel = widget.table._progress.findChild(QPushButton)
        assert cancel.isEnabled()
        qtbot.mouseClick(cancel, Qt.MouseButton.LeftButton)
        assert widget.table._worker.cancelled.is_set()
    finally:
        release.set()
        qtbot.waitUntil(lambda: not widget._busy)
    assert widget.table.rowCount() == 0
    assert widget.add_button.isEnabled()


def test_inline_matching_failure_allows_retry(qtbot, tmp_path, monkeypatch):
    from backend.database_worker import DatabaseWorker
    widget = CoreRenamerWidget()
    qtbot.addWidget(widget)
    source = tmp_path / 'Movie.mkv'
    source.touch()
    widget.table.add_records([MediaRecord(str(source))])
    widget.database_combo.setCurrentText('Local metadata')
    real_run = DatabaseWorker.run
    monkeypatch.setattr(DatabaseWorker, 'run', lambda worker: worker.error.emit('Request timed out. Try again.'))
    widget.match_files()
    qtbot.waitUntil(lambda: not widget._busy)
    assert 'timed out' in widget.info_text.text()
    assert widget.match_button.isEnabled()
    assert widget.table.isEnabled()
    monkeypatch.setattr(DatabaseWorker, 'run', real_run)
    widget.match_files()
    qtbot.waitUntil(lambda: not widget._busy)
    assert widget.table.item(0, 2).text()


def test_changed_match_settings_invalidate_preview(qtbot, tmp_path):
    widget = CoreRenamerWidget()
    qtbot.addWidget(widget)
    source, target = tmp_path / 'Show.S01E01.mkv', tmp_path / 'Pilot.mkv'
    prepare(widget, [source], [target])
    assert widget.rename_button.isEnabled()
    widget.database_combo.setCurrentText('Local metadata')
    assert widget.table.item(0, 2).text() == ''
    assert not widget.rename_button.isEnabled()
