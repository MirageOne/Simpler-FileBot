"""Fluent-style batch renaming workspace, backed by the existing metadata services."""
import copy
import os
from collections import Counter

from PySide6.QtCore import Qt, Signal, Slot, QThreadPool
from PySide6.QtGui import QColor, QBrush, QShortcut, QKeySequence, QIntValidator
from PySide6.QtWidgets import (QWidget, QVBoxLayout, QHBoxLayout, QGridLayout, QLabel, QPushButton,
                               QFrame, QComboBox, QLineEdit, QCheckBox, QFileDialog, QProgressBar)

from backend.core_backend import get_invalid_file_names_and_fixes, perform_file_renaming
from backend.database_worker import DatabaseWorker
from backend.formats_backend import retrieve_movies_format_from_formats_file, retrieve_series_format_from_formats_file
from backend.media_record import MediaRecord
from backend.settings_backend import retrieve_filename_analysis_only_flag, set_filename_analysis_only_flag
from databases.file_name_match_db import FileNameMatchDB
from databases.tvmaze_python_db import TVMazePythonDB
from databases.themoviedb_python_db import TheMovieDBPythonDB
from databases.omdb_python_db import OMDBPythonDB
from pages.core.match_options_widget import check_if_api_key_exists_otherwise_prompt_user
from pages.core.media_table import MediaTable
from ui.fluent import icon


def label(text, name=None):
    widget = QLabel(text)
    widget.setTextFormat(Qt.TextFormat.PlainText)
    if name:
        widget.setObjectName(name)
    return widget


def button(text, glyph=None, primary=False):
    widget = QPushButton(text)
    if glyph:
        widget.setIcon(icon(glyph, '#ffffff' if primary else '#748093'))
    if primary:
        widget.setObjectName('primaryButton')
    widget.setCursor(Qt.CursorShape.PointingHandCursor)
    return widget


class CoreRenamerWidget(QWidget):
    editFormatRequested = Signal()

    def __init__(self, parent=None):
        super().__init__(parent)
        self.setObjectName('renameWorkspace')
        self.last_renames = []
        self._busy = False
        self._matching = False
        self._ready = []
        self._warnings = []
        self._message = ''
        self._snapshot_paths = []
        layout = QVBoxLayout(self)
        layout.setContentsMargins(30, 24, 30, 24)
        layout.setSpacing(18)

        heading = QHBoxLayout()
        heading_text = QVBoxLayout()
        heading_text.setSpacing(4)
        heading_text.addWidget(label('Rename files', 'pageTitle'))
        heading_text.addWidget(label('Match your media. Preview every change.', 'secondaryText'))
        heading.addLayout(heading_text)
        heading.addStretch()
        self.undo_button = button('Undo last rename', 'undo')
        self.undo_button.setShortcut(QKeySequence.StandardKey.Undo)
        self.undo_button.clicked.connect(self.undo_last_rename_operation)
        heading.addWidget(self.undo_button)
        layout.addLayout(heading)

        command_bar = QHBoxLayout()
        command_bar.setSpacing(10)
        self.add_button = button('Add files', 'add', True)
        self.add_button.clicked.connect(self.browse_files)
        self.folder_button = button('Add folder', 'folder')
        self.folder_button.clicked.connect(self.browse_folder)
        self.remove_button = button('Remove', 'remove')
        self.remove_button.setObjectName('subtleButton')
        self.remove_button.clicked.connect(self.remove_selected)
        self.clear_button = button('Clear', 'clear')
        self.clear_button.setObjectName('subtleButton')
        self.clear_button.clicked.connect(self.clear_files)
        for control in (self.add_button, self.folder_button, self.remove_button, self.clear_button):
            command_bar.addWidget(control)
        command_bar.addStretch()
        self.file_count = label('No files added', 'secondaryText')
        command_bar.addWidget(self.file_count)
        layout.addLayout(command_bar)

        card = QFrame()
        card.setObjectName('previewCard')
        card_layout = QVBoxLayout(card)
        card_layout.setContentsMargins(0, 0, 0, 0)
        card_layout.setSpacing(0)
        self.options = QWidget()
        options_layout = QVBoxLayout(self.options)
        options_layout.setContentsMargins(18, 14, 18, 12)
        options_layout.setSpacing(12)
        fields = QGridLayout()
        fields.setHorizontalSpacing(14)
        fields.setVerticalSpacing(6)
        self.database_combo = QComboBox()
        self.database_combo.addItems(['TVMaze', 'TheMovieDB', 'OMDB', 'Local metadata'])
        self.database_combo.setAccessibleName('Metadata source')
        self.series_input = QLineEdit()
        self.series_input.setPlaceholderText('Detected from your files')
        self.series_input.setAccessibleName('Series title')
        self.year_input = QLineEdit()
        self.year_input.setPlaceholderText('Auto')
        self.year_input.setValidator(QIntValidator(1, 9999, self))
        self.year_input.setMaximumWidth(112)
        self.year_input.setAccessibleName('Series premiere year')
        self.match_button = button('Match files', 'match')
        self.match_button.setShortcut('Alt+M')
        self.match_button.clicked.connect(self.match_files)
        for column, text, control in ((0, 'Metadata source', self.database_combo),
                                       (1, 'Series', self.series_input), (2, 'Year', self.year_input)):
            fields.addWidget(label(text), 0, column)
            fields.addWidget(control, 1, column)
        fields.addWidget(self.match_button, 1, 3)
        fields.setColumnStretch(0, 2)
        fields.setColumnStretch(1, 3)
        options_layout.addLayout(fields)
        format_row = QHBoxLayout()
        self.format_label = label('', 'secondaryText')
        self.format_label.setWordWrap(True)
        format_row.addWidget(self.format_label, 1)
        self.edit_format = button('Edit format')
        self.edit_format.setObjectName('linkButton')
        self.edit_format.clicked.connect(self.editFormatRequested.emit)
        format_row.addWidget(self.edit_format)
        options_layout.addLayout(format_row)
        flags = QHBoxLayout()
        self.absolute_order = QCheckBox('Absolute episode order')
        self.filename_only = QCheckBox('Analyze filenames only')
        self.filename_only.setChecked(retrieve_filename_analysis_only_flag())
        self.filename_only.setToolTip('Applies to files added after this option changes.')
        self.filename_only.toggled.connect(set_filename_analysis_only_flag)
        flags.addWidget(self.absolute_order)
        flags.addWidget(self.filename_only)
        flags.addStretch()
        options_layout.addLayout(flags)
        card_layout.addWidget(self.options)

        self.table = MediaTable()
        self.table.recordsChanged.connect(self._records_changed)
        self.table.itemChanged.connect(self._item_changed)
        self.table.horizontalHeader().sectionClicked.connect(self.toggle_all)
        self.table.importBusy.connect(self.set_busy)
        self.table.importError.connect(self.show_message)
        card_layout.addWidget(self.table, 1)
        self.info_bar = QFrame()
        self.info_bar.setObjectName('infoBar')
        info_layout = QHBoxLayout(self.info_bar)
        info_layout.setContentsMargins(16, 10, 16, 10)
        self.info_text = label('')
        self.info_text.setWordWrap(True)
        info_layout.addWidget(self.info_text, 1)
        self.review_button = button('Review file')
        self.review_button.setObjectName('linkButton')
        self.review_button.clicked.connect(self.review_file)
        info_layout.addWidget(self.review_button)
        card_layout.addWidget(self.info_bar)
        path_row = QHBoxLayout()
        path_row.setContentsMargins(18, 10, 18, 10)
        folder_icon = QLabel()
        folder_icon.setPixmap(icon('folder', '#748093').pixmap(18, 18))
        path_row.addWidget(folder_icon)
        self.folder_label = label('No folder selected', 'secondaryText')
        path_row.addWidget(self.folder_label, 1)
        path_row.addWidget(label('Files stay in their current folder', 'secondaryText'))
        card_layout.addLayout(path_row)
        layout.addWidget(card, 1)

        footer = QHBoxLayout()
        footer_text = QVBoxLayout()
        footer_text.setSpacing(3)
        self.summary = label('Add files to begin')
        footer_text.addWidget(self.summary)
        footer_text.addWidget(label('Existing files are never replaced.', 'secondaryText'))
        footer.addLayout(footer_text, 1)
        self.cancel_button = button('Cancel')
        self.cancel_button.clicked.connect(self.clear_preview)
        self.rename_button = button('Rename files', 'rename', True)
        self.rename_button.clicked.connect(self.rename_files_if_allowed)
        footer.addWidget(self.cancel_button)
        footer.addWidget(self.rename_button)
        layout.addLayout(footer)
        self.progress = QProgressBar()
        self.progress.setRange(0, 0)
        self.progress.setMaximumHeight(3)
        self.progress.setTextVisible(False)
        self.progress.hide()
        layout.addWidget(self.progress)
        self._shortcuts = []
        for key, action in (('Ctrl+O', self.browse_files), ('Ctrl+Shift+O', self.browse_folder),
                             ('Delete', self.remove_selected)):
            shortcut = QShortcut(QKeySequence(key), self)
            shortcut.activated.connect(action)
            self._shortcuts.append(shortcut)
        self.database_combo.currentTextChanged.connect(lambda: self.clear_preview())
        self.series_input.textEdited.connect(lambda: self.clear_preview())
        self.year_input.textEdited.connect(lambda: self.clear_preview())
        self.absolute_order.toggled.connect(lambda: self.clear_preview())
        self._last_format = ''
        self._records_changed()

    def records(self):
        return [self.table.record(row) for row in range(self.table.rowCount())]

    def add_files(self, paths):
        if not self._matching:
            self.table.import_paths(paths)

    def browse_files(self):
        if not self._busy:
            paths, _ = QFileDialog.getOpenFileNames(self, 'Add media files')
            self.add_files(paths)

    def browse_folder(self):
        if not self._busy:
            path = QFileDialog.getExistingDirectory(self, 'Add media folder')
            if path:
                self.add_files([path])

    def remove_selected(self):
        if not self._busy:
            self.table.remove_rows([item.row() for item in self.table.selectionModel().selectedRows()])

    def clear_files(self):
        if not self._busy:
            self._message = ''
            self.table.remove_rows(range(self.table.rowCount()))

    def _records_changed(self):
        records = self.records()
        series = bool(records) and MediaRecord.is_tv_series(records)
        self.series_input.setEnabled(series and not self._busy)
        self.year_input.setEnabled(series and not self._busy)
        self.absolute_order.setEnabled(series and not self._busy)
        titles = MediaRecord.get_unique_titles(records) if series else set()
        self.series_input.setText(next(iter(titles)) if len(titles) == 1 else '')
        self.year_input.setText(str(records[0].year) if series and records[0].year else '')
        if records and not series and self.database_combo.currentText() == 'TVMaze':
            self.database_combo.setCurrentText('TheMovieDB')
        self.update_format_label()
        self.refresh_preview()

    def update_format_label(self):
        series = MediaRecord.is_tv_series(self.records())
        template = retrieve_series_format_from_formats_file() if series else retrieve_movies_format_from_formats_file()
        if self._last_format and template != self._last_format:
            self.clear_preview()
        self._last_format = template
        self.format_label.setText('Format: ' + template)
        self.format_label.setToolTip(template)

    def showEvent(self, event):
        super().showEvent(event)
        self.update_format_label()

    def _item_changed(self, item):
        if item.column() == 2:
            self.table.blockSignals(True)
            self.table.item(item.row(), 0).setCheckState(Qt.CheckState.Checked)
            self.table.blockSignals(False)
        self._message = ''
        self.refresh_preview()

    @staticmethod
    def _target_key(path):
        return os.path.normcase(os.path.abspath(path))

    def refresh_preview(self):
        self.table.blockSignals(True)
        selected_targets = Counter()
        for row in range(self.table.rowCount()):
            name = self.table.item(row, 2).text()
            if name and self.table.item(row, 0).checkState() == Qt.CheckState.Checked:
                target = os.path.join(os.path.dirname(self.table.record(row).full_file_path), name)
                selected_targets[self._target_key(target)] += 1
        self._ready, self._warnings = [], []
        dark = self.palette().window().color().lightness() < 128
        warning_bg = QColor('#493c21' if dark else '#fff8e5')
        green = '#7dd899' if dark else '#24833e'
        amber = '#f3c15b' if dark else '#986600'
        for row in range(self.table.rowCount()):
            name = self.table.item(row, 2).text()
            source = self.table.record(row).full_file_path
            target = os.path.join(os.path.dirname(source), name)
            same_path = self._target_key(source) == self._target_key(target)
            reason = ''
            status = 'Ready'
            if not name:
                status = 'Not matched'
            elif '[Missing metadata]' in name or '{None}' in name:
                reason = 'Missing metadata. Edit the new filename or match with another provider.'
            elif get_invalid_file_names_and_fixes([name]) or name.rstrip(' .') != name or name in ('.', '..'):
                reason = 'Invalid filename. Double-click the new filename to edit it.'
            elif name.split('.')[0].upper() in {'CON', 'PRN', 'AUX', 'NUL', *(f'COM{i}' for i in range(1, 10)), *(f'LPT{i}' for i in range(1, 10))}:
                reason = 'This filename is reserved by Windows.'
            elif any(ord(character) < 32 for character in name):
                reason = 'The filename contains a control character.'
            elif selected_targets[self._target_key(target)] > 1:
                reason = 'Multiple selected files have this destination. Edit a filename or deselect a file.'
            elif not same_path and os.path.lexists(target):
                reason = 'A file already exists at this destination.'
            elif same_path:
                status = 'Unchanged'
            if reason:
                status = 'Review'
                self._warnings.append(row)
            elif status == 'Ready':
                self._ready.append(row)
            cell = self.table.item(row, 3)
            cell.setText(status)
            cell.setData(Qt.ItemDataRole.UserRole, reason)
            cell.setToolTip(reason)
            cell.setIcon(icon('warning', amber) if reason else icon('check', green) if status == 'Ready' else icon('file', '#8993a2'))
            cell.setForeground(QBrush(QColor(amber if reason else green)) if status in ('Ready', 'Review') else QBrush(self.palette().placeholderText().color()))
            for col in range(4):
                self.table.item(row, col).setBackground(QBrush(warning_bg) if reason else QBrush())
            self.table.item(row, 1).setForeground(QBrush(QColor('#aab3c2' if dark else '#657080')))
            self.table.item(row, 2).setToolTip(reason or name or 'Match files to generate a preview')
        self.table.blockSignals(False)
        selected = self.selected_ready_rows()
        count = self.table.rowCount()
        media = 'TV episodes' if MediaRecord.is_tv_series(self.records()) else 'Movies'
        self.file_count.setText(f'{count} file{"s" if count != 1 else ""} added · {media}' if count else 'No files added')
        self.summary.setText(f'{len(selected)} file{"s" if len(selected) != 1 else ""} ready to rename' if count else 'Add files to begin')
        self.rename_button.setText(f'Rename {len(selected)} files' if selected else 'Rename files')
        self.rename_button.setEnabled(bool(selected) and not self._busy)
        self.match_button.setEnabled(bool(count) and not self._busy)
        self.cancel_button.setEnabled(any(self.table.item(r, 2).text() for r in range(count)) and not self._busy)
        self.undo_button.setEnabled(bool(self.last_renames) and not self._busy)
        self.remove_button.setEnabled(bool(count) and not self._busy)
        self.clear_button.setEnabled(bool(count) and not self._busy)
        directories = {os.path.dirname(record.full_file_path) for record in self.records()}
        folder = next(iter(directories)) if len(directories) == 1 else f'{len(directories)} folders' if directories else 'No folder selected'
        self.folder_label.setText(self.fontMetrics().elidedText(folder, Qt.TextElideMode.ElideMiddle, max(160, self.width() - 550)))
        self.folder_label.setToolTip('\n'.join(sorted(directories)))
        self.info_bar.setVisible(bool(self._message or self._warnings))
        self.review_button.setVisible(bool(self._warnings) and not self._message)
        warnings = len(self._warnings)
        self.info_text.setText(self._message or f'{warnings} file{"s" if warnings != 1 else ""} {"needs" if warnings == 1 else "need"} review. Files needing review will be skipped.')

    def selected_ready_rows(self):
        return [row for row in self._ready if self.table.item(row, 0).checkState() == Qt.CheckState.Checked]

    def toggle_all(self, section):
        if section != 0 or self._busy:
            return
        checked = bool(self.selected_ready_rows())
        self.table.blockSignals(True)
        for row in range(self.table.rowCount()):
            self.table.item(row, 0).setCheckState(Qt.CheckState.Unchecked if checked else Qt.CheckState.Checked)
        self.table.blockSignals(False)
        self.refresh_preview()

    def review_file(self):
        if self._warnings:
            row = self._warnings[0]
            self.table.setCurrentCell(row, 2)
            self.table.editItem(self.table.item(row, 2))

    def clear_preview(self):
        if self._busy:
            return
        self.table.blockSignals(True)
        for row in range(self.table.rowCount()):
            self.table.item(row, 2).setText('')
            self.table.item(row, 0).setCheckState(Qt.CheckState.Unchecked)
        self.table.blockSignals(False)
        self._message = ''
        self.refresh_preview()

    def set_busy(self, busy):
        self._busy = busy
        for control in (self.add_button, self.folder_button, self.options):
            control.setEnabled(not busy)
        self.table.setEnabled(not self._matching)
        self.progress.setVisible(busy)
        self.refresh_preview()
        if busy:
            self.summary.setText('Matching metadata…' if self._matching else 'Importing files…')
        else:
            series = MediaRecord.is_tv_series(self.records())
            for control in (self.series_input, self.year_input, self.absolute_order):
                control.setEnabled(series)

    def show_message(self, message):
        self._message = message
        self.refresh_preview()

    def match_files(self):
        if self._busy or not self.records():
            return
        records = copy.deepcopy(self.records())
        if MediaRecord.has_movies(records) and MediaRecord.has_episodes(records):
            self.show_message('Match movies and TV episodes in separate batches.')
            return
        series = MediaRecord.is_tv_series(records)
        if series:
            if self.year_input.text() and not self.year_input.hasAcceptableInput():
                self.show_message('Enter a year from 1 to 9999, or leave Year empty for automatic matching.')
                return
            if len(MediaRecord.get_unique_titles(records)) > 1 and not self.series_input.text().strip():
                self.show_message('This batch contains multiple series. Enter one series title or remove unrelated files.')
                return
            if self.series_input.text().strip():
                MediaRecord.update_title_for_all_records(self.series_input.text().strip(), records)
            for record in records:
                record.year = int(self.year_input.text()) if self.year_input.text() else None
                record.is_absolute_order = self.absolute_order.isChecked()
        source = self.database_combo.currentText()
        if source == 'TVMaze' and not series:
            self.show_message('TVMaze supports TV episodes only. Choose another metadata source for movies.')
            return
        key = {'TheMovieDB': 'the_movie_db', 'OMDB': 'omdb'}.get(source)
        if key and not check_if_api_key_exists_otherwise_prompt_user(key):
            return
        database = {'TVMaze': TVMazePythonDB, 'TheMovieDB': TheMovieDBPythonDB,
                    'OMDB': OMDBPythonDB, 'Local metadata': FileNameMatchDB}[source](records, series)
        self._message = ''
        self._snapshot_paths = [record.full_file_path for record in records]
        self._matching = True
        self.set_busy(True)
        self._match_worker = DatabaseWorker(database)
        self._match_worker.finished.connect(self.apply_matches)
        self._match_worker.error.connect(self.match_failed)
        QThreadPool.globalInstance().start(self._match_worker)

    @Slot(list)
    def apply_matches(self, titles):
        self._matching = False
        self.set_busy(False)
        if self._snapshot_paths and self._snapshot_paths != [record.full_file_path for record in self.records()]:
            self._snapshot_paths = []
            self.show_message('The file list changed while matching. Please match again.')
            return
        self._snapshot_paths = []
        if len(titles) != self.table.rowCount():
            self.show_message('The provider returned an incomplete result. Please try matching again.')
            return
        self.table.blockSignals(True)
        for row, title in enumerate(titles):
            self.table.item(row, 2).setText(title.replace('{None}', '[Missing metadata]'))
            self.table.item(row, 0).setCheckState(Qt.CheckState.Unchecked if '{None}' in title else Qt.CheckState.Checked)
        self.table.blockSignals(False)
        self.refresh_preview()

    @Slot(str)
    def match_failed(self, message):
        self._matching = False
        self._snapshot_paths = []
        self.set_busy(False)
        self.show_message(message)

    def _update_paths(self, completed):
        by_source = {source: target for target, source in completed}
        self.table.blockSignals(True)
        for row, record in enumerate(self.records()):
            if record.full_file_path in by_source:
                record.full_file_path = by_source[record.full_file_path]
                record.file_name = os.path.basename(record.full_file_path)
                self.table.item(row, 1).setText(record.file_name)
                self.table.item(row, 1).setToolTip(record.full_file_path)
                self.table.item(row, 2).setText('')
                self.table.item(row, 0).setCheckState(Qt.CheckState.Unchecked)
        self.table.blockSignals(False)

    def rename_files_if_allowed(self):
        if self._busy:
            return
        self.refresh_preview()  # Recheck destinations immediately before touching disk.
        rows = self.selected_ready_rows()
        if not rows:
            return
        old = [self.table.record(row).full_file_path for row in rows]
        new = [os.path.join(os.path.dirname(path), self.table.item(row, 2).text()) for row, path in zip(rows, old)]
        completed = []
        try:
            perform_file_renaming(old, new, completed)
            self._message = f'Renamed {len(completed)} files. You can undo this operation.'
        except (ValueError, OSError) as error:
            self._message = f'Renamed {len(completed)} of {len(rows)} files. {error} Completed changes can be undone.'
        finally:
            if completed:
                self.last_renames = completed
                self._update_paths(completed)
            self.refresh_preview()

    def undo_last_rename_operation(self):
        if self._busy or not self.last_renames:
            return
        completed = []
        renamed, originals = map(list, zip(*self.last_renames))
        try:
            perform_file_renaming(renamed, originals, completed)
            self._message = f'Undid {len(completed)} renames.'
        except (ValueError, OSError) as error:
            self._message = f'Could not finish undoing: {error} You can retry Undo.'
        finally:
            undone = {(source, target) for target, source in completed}
            self.last_renames = [pair for pair in self.last_renames if pair not in undone]
            self._update_paths(completed)
            self.refresh_preview()
