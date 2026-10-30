from PySide6.QtCore import Qt, QPoint, QThreadPool, Slot
from PySide6.QtGui import QIcon, QPixmap
from PySide6.QtWidgets import QListWidget, QListWidgetItem, QMenu, QDialog, QVBoxLayout, QLabel, QProgressDialog, QApplication

from backend.media_record import MediaRecord
from backend.file_import_worker import FileImportWorker
from backend.settings_backend import ensure
from backend.error_popup_widget import ErrorPopupWidget
from backend.utils import resource_path


class DragAndDropFilesWidget(QListWidget):
    """
    QListWidget subclass to allow for drag-and-drop files functionality.
    dragEnterEvent(), dragMoveEvent(), and dropEvent() need to be overridden for that to work.
    """

    def __init__(self, parent=None):
        super().__init__(parent)
        self.setAcceptDrops(True)
        self._import_worker = None
        self._import_progress = None
        self._pending_paths = []
        self._import_error = ""

        # Allow the user to right-click a QListWidgetItem to show information about the MediaRecord.
        self.setContextMenuPolicy(Qt.ContextMenuPolicy.CustomContextMenu)
        self.customContextMenuRequested.connect(self.show_context_menu_on_right_click)

    def dragEnterEvent(self, event):
        if event.mimeData().hasUrls():
            event.acceptProposedAction()

    def dragMoveEvent(self, event):
        if event.mimeData().hasUrls():
            event.acceptProposedAction()

    def dropEvent(self, event):
        if event.mimeData().hasUrls():
            self.import_paths([url.toLocalFile() for url in event.mimeData().urls() if url.isLocalFile()])

            event.acceptProposedAction()

    def add_path(self, file_path: str):
        self.import_paths([file_path])

    def import_paths(self, paths: list[str]):
        if not paths:
            return
        if self._import_worker is not None:
            self._pending_paths.extend(paths)
            return
        # Qt style hints must be initialized on the GUI thread before parsing records.
        ensure()
        self._import_error = ""
        worker = FileImportWorker(list(paths))
        self._import_worker = worker
        QApplication.instance().aboutToQuit.connect(worker.cancelled.set)
        self.destroyed.connect(worker.cancelled.set)
        progress = QProgressDialog("Scanning folders and analyzing filenames…", "Cancel", 0, 0, self)
        progress.setWindowTitle("Import Files")
        progress.setWindowModality(Qt.WindowModality.WindowModal)
        progress.setMinimumDuration(0)
        progress.canceled.connect(self.cancel_import)
        self._import_progress = progress
        worker.signals.records.connect(self._add_imported_records)
        worker.signals.progress.connect(self._update_import_progress)
        worker.signals.error.connect(self._record_import_error)
        worker.signals.finished.connect(self._finish_import)
        progress.show()
        QThreadPool.globalInstance().start(worker)

    @Slot()
    def cancel_import(self):
        if self._import_worker is not None:
            self._import_worker.cancelled.set()
        self._pending_paths.clear()

    @Slot(list)
    def _add_imported_records(self, records: list[MediaRecord]):
        if self._import_worker is None or self._import_worker.cancelled.is_set():
            return
        for record in records:
            self._add_record(record)

    @Slot(int)
    def _update_import_progress(self, count: int):
        if self._import_progress is not None:
            self._import_progress.setLabelText(f"Analyzed {count} files… Cancel keeps files already added.")

    @Slot(str)
    def _record_import_error(self, message: str):
        self._import_error = message

    @Slot()
    def _finish_import(self):
        QApplication.instance().aboutToQuit.disconnect(self._import_worker.cancelled.set)
        self.destroyed.disconnect(self._import_worker.cancelled.set)
        self._import_worker = None
        self._import_progress.canceled.disconnect(self.cancel_import)
        self._import_progress.close()
        self._import_progress.deleteLater()
        self._import_progress = None
        pending, self._pending_paths = self._pending_paths, []
        if self._import_error:
            ErrorPopupWidget(self._import_error, self).exec()
        if pending:
            self.import_paths(pending)

    def add_file_to_list(self, file_path: str):
        """Synchronously add one record; use import_paths for user-selected batches."""
        self._add_record(MediaRecord(file_path))

    def _add_record(self, media_record: MediaRecord):
        list_item = QListWidgetItem(media_record.file_name)
        list_item.setData(Qt.ItemDataRole.UserRole, media_record)
        self.addItem(list_item)

    def show_context_menu_on_right_click(self, position: QPoint):
        """Show a context menu when a user right-clicks on a QListWidgetItem."""
        item = self.itemAt(position)
        if item is None:
            return

        # Recover the MediaRecord data from the QListWidgetItem.
        media_record: MediaRecord = item.data(Qt.ItemDataRole.UserRole)

        context_menu = QMenu(self)
        metadata_choice = context_menu.addAction("Show metadata")
        action = context_menu.exec(self.viewport().mapToGlobal(position))

        if action == metadata_choice:
            self.__show_metadata_dialog(media_record)

    @staticmethod
    def __show_metadata_dialog(media_record: MediaRecord):
        metadata_dialog = QDialog()
        metadata_dialog.setWindowIcon(QIcon(QPixmap(resource_path("resources/Alternative App Logo.png"))))
        metadata_dialog.setWindowTitle(media_record.file_name)

        metadata_dialog_layout = QVBoxLayout(metadata_dialog)

        for key, value in media_record.metadata.items():
            metadata_dialog_layout.addWidget(QLabel(f"{key}: {value}"))

        metadata_dialog.exec()
