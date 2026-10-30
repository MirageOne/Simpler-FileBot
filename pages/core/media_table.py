"""Unified, editable rename preview with asynchronous file import."""
import os

from PySide6.QtGui import QColor

from PySide6.QtCore import Qt, Signal, Slot, QThreadPool
from PySide6.QtWidgets import (QTableWidget, QTableWidgetItem, QHeaderView, QAbstractItemView,
                               QLabel, QProgressDialog, QApplication, QStyledItemDelegate)

from backend.file_import_worker import FileImportWorker
from backend.settings_backend import ensure
from ui.fluent import icon


class PreviewDelegate(QStyledItemDelegate):
    def paint(self, painter, option, index):
        super().paint(painter, option, index)
        reason = index.siblingAtColumn(3).data(Qt.ItemDataRole.UserRole)
        if reason:
            # A translucent wash preserves text, selection and keyboard focus rendering.
            painter.save()
            painter.fillRect(option.rect, QColor(230, 168, 0, 24))
            painter.restore()


class MediaTable(QTableWidget):
    recordsChanged = Signal()
    importBusy = Signal(bool)
    importError = Signal(str)

    def __init__(self, parent=None):
        super().__init__(0, 4, parent)
        self.setObjectName('mediaTable')
        self.setItemDelegate(PreviewDelegate(self))
        self.horizontalHeader().setDefaultAlignment(Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignVCenter)
        self.setHorizontalHeaderLabels(['✓', 'Original filename', 'New filename', 'Status'])
        self.horizontalHeaderItem(0).setToolTip('Select or deselect all ready files')
        self.horizontalHeader().setSectionResizeMode(0, QHeaderView.ResizeMode.Fixed)
        self.horizontalHeader().setSectionResizeMode(1, QHeaderView.ResizeMode.Stretch)
        self.horizontalHeader().setSectionResizeMode(2, QHeaderView.ResizeMode.Stretch)
        self.horizontalHeader().setSectionResizeMode(3, QHeaderView.ResizeMode.Fixed)
        self.setColumnWidth(0, 44)
        self.setColumnWidth(3, 120)
        self.verticalHeader().hide()
        self.verticalHeader().setDefaultSectionSize(44)
        self.setShowGrid(False)
        self.setSelectionBehavior(QAbstractItemView.SelectionBehavior.SelectRows)
        self.setSelectionMode(QAbstractItemView.SelectionMode.ExtendedSelection)
        self.setEditTriggers(QAbstractItemView.EditTrigger.DoubleClicked | QAbstractItemView.EditTrigger.EditKeyPressed)
        self.setSortingEnabled(False)
        self.setAcceptDrops(True)
        self.setDragDropMode(QAbstractItemView.DragDropMode.DropOnly)
        self.setAccessibleName('File rename preview')
        self.setToolTip('Drop files or folders here. Double-click a new filename to edit it.')
        self.empty_label = QLabel('Add your media files\n\nDrop files or folders here, or choose Add files to get started.', self.viewport())
        self.empty_label.setObjectName('emptyState')
        self.empty_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self.empty_label.setAttribute(Qt.WidgetAttribute.WA_TransparentForMouseEvents)
        self._worker = None
        self._progress = None
        self._pending = []
        self._error = ''

    def resizeEvent(self, event):
        super().resizeEvent(event)
        self.empty_label.setGeometry(self.viewport().rect())

    def dragEnterEvent(self, event):
        if event.mimeData().hasUrls() and self.isEnabled():
            event.acceptProposedAction()

    def dragMoveEvent(self, event):
        self.dragEnterEvent(event)

    def dropEvent(self, event):
        self.import_paths([url.toLocalFile() for url in event.mimeData().urls() if url.isLocalFile()])
        event.acceptProposedAction()

    def record(self, row):
        return self.item(row, 1).data(Qt.ItemDataRole.UserRole)

    def add_records(self, records):
        existing = {os.path.normcase(os.path.abspath(self.record(row).full_file_path)) for row in range(self.rowCount())}
        self.blockSignals(True)
        for record in records:
            key = os.path.normcase(os.path.abspath(record.full_file_path))
            if key in existing:
                continue
            existing.add(key)
            row = self.rowCount()
            self.insertRow(row)
            checkbox = QTableWidgetItem()
            checkbox.setFlags(Qt.ItemFlag.ItemIsEnabled | Qt.ItemFlag.ItemIsSelectable | Qt.ItemFlag.ItemIsUserCheckable)
            checkbox.setCheckState(Qt.CheckState.Unchecked)
            original = QTableWidgetItem(icon('file', '#7e8998'), record.file_name)
            original.setData(Qt.ItemDataRole.UserRole, record)
            original.setForeground(self.palette().placeholderText())
            original.setToolTip(record.full_file_path)
            original.setFlags(Qt.ItemFlag.ItemIsEnabled | Qt.ItemFlag.ItemIsSelectable)
            status = QTableWidgetItem('Not matched')
            status.setFlags(original.flags())
            self.setItem(row, 0, checkbox)
            self.setItem(row, 1, original)
            self.setItem(row, 2, QTableWidgetItem(''))
            self.setItem(row, 3, status)
        self.blockSignals(False)
        self.empty_label.setVisible(self.rowCount() == 0)
        self.recordsChanged.emit()

    def remove_rows(self, rows):
        self.blockSignals(True)
        for row in sorted(set(rows), reverse=True):
            self.removeRow(row)
        self.blockSignals(False)
        self.empty_label.setVisible(self.rowCount() == 0)
        self.recordsChanged.emit()

    def import_paths(self, paths):
        if not paths:
            return
        if self._worker is not None:
            self._pending.extend(paths)
            return
        ensure()
        self._error = ''
        self._worker = FileImportWorker(list(paths))
        QApplication.instance().aboutToQuit.connect(self._worker.cancelled.set)
        self.destroyed.connect(self._worker.cancelled.set)
        self._progress = QProgressDialog('Scanning folders and analyzing filenames…', 'Cancel', 0, 0, self)
        self._progress.setWindowTitle('Import files')
        self._progress.setWindowModality(Qt.WindowModality.WindowModal)
        self._progress.setMinimumDuration(0)
        self._progress.canceled.connect(self.cancel_import)
        self._worker.signals.records.connect(self._receive_records)
        self._worker.signals.progress.connect(self._import_progress)
        self._worker.signals.error.connect(self._import_error)
        self._worker.signals.finished.connect(self._finish_import)
        self.importBusy.emit(True)
        self._progress.show()
        QThreadPool.globalInstance().start(self._worker)

    @Slot()
    def cancel_import(self):
        if self._worker:
            self._worker.cancelled.set()
        self._pending.clear()

    @Slot(list)
    def _receive_records(self, records):
        if self._worker and not self._worker.cancelled.is_set():
            self.add_records(records)

    @Slot(int)
    def _import_progress(self, count):
        self._progress.setLabelText(f'Analyzed {count} files. Cancel keeps files already added.')

    @Slot(str)
    def _import_error(self, message):
        self._error = message

    @Slot()
    def _finish_import(self):
        QApplication.instance().aboutToQuit.disconnect(self._worker.cancelled.set)
        self.destroyed.disconnect(self._worker.cancelled.set)
        self._progress.canceled.disconnect(self.cancel_import)
        self._progress.close()
        self._progress.deleteLater()
        self._progress = None
        self._worker = None
        self.importBusy.emit(False)
        if self._error:
            self.importError.emit(self._error)
        pending, self._pending = self._pending, []
        if pending:
            self.import_paths(pending)
