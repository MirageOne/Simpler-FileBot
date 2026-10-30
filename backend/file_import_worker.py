"""Scan folders and parse filenames without blocking Qt's GUI thread."""
import os
from pathlib import Path
from threading import Event

from PySide6.QtCore import QObject, QRunnable, Signal

from backend.media_record import MediaRecord


class ImportSignals(QObject):
    records = Signal(list)
    progress = Signal(int)
    error = Signal(str)
    finished = Signal()


class FileImportWorker(QRunnable):
    def __init__(self, paths: list[str]):
        super().__init__()
        self.paths = paths
        self.signals = ImportSignals()
        self.cancelled = Event()

    def _files(self):
        def scan_error(error):
            raise error

        for name in self.paths:
            if self.cancelled.is_set():
                return
            path = Path(name)
            if path.is_file():
                yield str(path)
            elif path.is_dir():
                for directory, _, files in os.walk(path, onerror=scan_error):
                    if self.cancelled.is_set():
                        return
                    for filename in files:
                        if self.cancelled.is_set():
                            return
                        yield os.path.join(directory, filename)

    def run(self):
        batch = []
        count = 0
        try:
            for filename in self._files():
                if self.cancelled.is_set():
                    break
                record = MediaRecord(filename)
                if self.cancelled.is_set():
                    break
                batch.append(record)
                count += 1
                if len(batch) >= 25:
                    self.signals.records.emit(batch)
                    self.signals.progress.emit(count)
                    batch = []
        except Exception as error:  # Report failures to the GUI and always release its busy state.
            if not self.cancelled.is_set():
                self.signals.error.emit(f"Could not import files: {error}")
        finally:
            if batch and not self.cancelled.is_set():
                self.signals.records.emit(batch)
                self.signals.progress.emit(count)
            self.signals.finished.emit()
