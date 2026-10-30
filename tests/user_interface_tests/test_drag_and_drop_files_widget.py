from pathlib import Path

from PySide6.QtCore import QMimeData, QUrl, QPoint
from PySide6.QtGui import QDropEvent, Qt
from pytestqt.qtbot import QtBot
from PySide6.QtWidgets import QLabel

from backend.media_record import MediaRecord
from pages.core.drag_and_drop_files_widget import DragAndDropFilesWidget


def _make_folder_with_files_and_subfolders(tmp_path: Path):
    """Add files and folders with files to a given folder and return a list of all files."""
    file1 = tmp_path / "a.txt"
    file1.touch()

    file2 = tmp_path / "Example Movie.mkv"
    file2.touch()

    subfolder1 = tmp_path / "subfolder1"
    subfolder1.mkdir()

    file3 = subfolder1 / "Iron Man (2008).mkv"
    file3.touch()

    subfolder2 = subfolder1 / "subfolder2"
    subfolder2.mkdir()

    file4 = subfolder2 / "Thunderbolts (2025).mkv"
    file4.touch()

    (tmp_path / "empty").mkdir()
    return file1.name, file2.name, file3.name, file4.name


def test_drop_directory_adds_all_files_recursively(qtbot: QtBot, tmp_path: Path):
    drag_and_drop_widget = DragAndDropFilesWidget()
    qtbot.addWidget(drag_and_drop_widget)
    files = _make_folder_with_files_and_subfolders(tmp_path)

    # Tests the DragAndDropFilesWidget with folders and files in tmp_path.
    test_payload = QMimeData()
    test_payload.setUrls([QUrl.fromLocalFile(str(tmp_path))])
    drop_event = QDropEvent(
        QPoint(0, 0),
        Qt.DropAction.CopyAction,
        test_payload,
        Qt.MouseButton.LeftButton,
        Qt.KeyboardModifier.NoModifier
    )

    drag_and_drop_widget.dropEvent(drop_event)
    qtbot.waitUntil(lambda: drag_and_drop_widget._import_worker is None, timeout=10000)

    file_names_in_widget = sorted(drag_and_drop_widget.item(i).text() for i in range(drag_and_drop_widget.count()))
    file_names_from_tmp_folder = sorted(file_name for file_name in files)
    assert file_names_in_widget == file_names_from_tmp_folder


def test_drag_and_drop_event_stores_file_name(qtbot: QtBot, tmp_path: Path):
    """Simulates an OS-style drop into the widget"""
    drag_and_drop_widget = DragAndDropFilesWidget()
    qtbot.addWidget(drag_and_drop_widget)
    temp_file = tmp_path / "The.West.Wing.S01E01.mkv"
    temp_file.touch()
    # Create the payload that'll be used in the QDropEvent.
    test_payload = QMimeData()
    test_payload.setUrls([QUrl.fromLocalFile(str(temp_file))])
    # Create a QDropEvent with our test payload.
    drop_event = QDropEvent(
        QPoint(0, 0),
        Qt.DropAction.CopyAction,
        test_payload,
        Qt.MouseButton.LeftButton,
        Qt.KeyboardModifier.NoModifier
    )

    drag_and_drop_widget.dropEvent(drop_event)
    qtbot.waitUntil(lambda: drag_and_drop_widget._import_worker is None, timeout=10000)

    assert drag_and_drop_widget.count() == 1
    assert drag_and_drop_widget.item(0).text() == "The.West.Wing.S01E01.mkv"


def test_add_file_to_list_stores_media_record(qtbot: QtBot, tmp_path: Path):
    drag_and_drop_widget = DragAndDropFilesWidget()
    qtbot.addWidget(drag_and_drop_widget)
    temp_file_path = tmp_path / "Andor.S02E09.mkv"

    drag_and_drop_widget.add_file_to_list(str(temp_file_path))

    list_item = drag_and_drop_widget.item(0)
    data = list_item.data(Qt.ItemDataRole.UserRole)
    assert list_item.text() == "Andor.S02E09.mkv"
    assert isinstance(data, MediaRecord)
    assert data.full_file_path == str(temp_file_path)


def test_import_keeps_gui_responsive_and_can_be_cancelled(qtbot, tmp_path, monkeypatch):
    from threading import Event
    from PySide6.QtCore import QTimer
    from PySide6.QtWidgets import QPushButton
    import backend.file_import_worker as worker_module

    widget = DragAndDropFilesWidget()
    qtbot.addWidget(widget)
    started, release = Event(), Event()
    real_record = worker_module.MediaRecord

    def slow_record(filename):
        started.set()
        if not release.wait(5):
            raise RuntimeError('Test failed to release parser')
        return real_record(filename)

    monkeypatch.setattr(worker_module, 'MediaRecord', slow_record)
    for index in range(3):
        (tmp_path / f'Movie {index}.mkv').touch()
    widget.import_paths([str(tmp_path)])
    try:
        qtbot.waitUntil(started.is_set)
        ticks = []
        QTimer.singleShot(0, lambda: ticks.append(True))
        qtbot.waitUntil(lambda: bool(ticks))
        cancel_button = widget._import_progress.findChild(QPushButton)
        qtbot.mouseClick(cancel_button, Qt.MouseButton.LeftButton)
        assert widget._import_worker.cancelled.is_set()
    finally:
        release.set()
        qtbot.waitUntil(lambda: widget._import_worker is None)
    assert widget.count() == 0
    assert widget._import_progress is None


def test_import_failure_shows_error_and_releases_busy_state(qtbot, tmp_path, monkeypatch):
    import backend.file_import_worker as worker_module
    widget = DragAndDropFilesWidget()
    qtbot.addWidget(widget)
    (tmp_path / 'movie.mkv').touch()

    def failed_record(filename):
        raise PermissionError('File is unavailable')

    messages = []
    monkeypatch.setattr(worker_module, 'MediaRecord', failed_record)
    monkeypatch.setattr('pages.core.drag_and_drop_files_widget.ErrorPopupWidget.exec',
                        lambda self: messages.append(self.findChild(QLabel).text()))
    widget.import_paths([str(tmp_path)])
    qtbot.waitUntil(lambda: widget._import_worker is None)
    assert widget._import_progress is None
    assert len(messages) == 1
    assert 'unavailable' in messages[0]


def test_import_queues_additional_paths(qtbot, tmp_path):
    widget = DragAndDropFilesWidget()
    qtbot.addWidget(widget)
    paths = [tmp_path / 'First.mkv', tmp_path / 'Second.mkv']
    for path in paths:
        path.touch()
    widget.import_paths([str(paths[0])])
    widget.import_paths([str(paths[1])])
    qtbot.waitUntil(lambda: widget._import_worker is None and not widget._pending_paths)
    assert [widget.item(index).text() for index in range(widget.count())] == [path.name for path in paths]


def test_cancelling_import_keeps_completed_batches(qtbot, tmp_path, monkeypatch):
    from threading import Event
    import backend.file_import_worker as worker_module
    widget = DragAndDropFilesWidget()
    qtbot.addWidget(widget)
    release = Event()
    calls = []
    real_record = worker_module.MediaRecord

    def pause_after_batch(filename):
        calls.append(filename)
        if len(calls) == 26 and not release.wait(5):
            raise RuntimeError('Test failed to release parser')
        return real_record(filename)

    monkeypatch.setattr(worker_module, 'MediaRecord', pause_after_batch)
    for index in range(30):
        (tmp_path / f'Movie {index}.mkv').touch()
    widget.import_paths([str(tmp_path)])
    try:
        qtbot.waitUntil(lambda: widget.count() == 25)
        widget.cancel_import()
    finally:
        release.set()
        qtbot.waitUntil(lambda: widget._import_worker is None)
    assert widget.count() == 25
