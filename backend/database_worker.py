from requests.exceptions import HTTPError, Timeout, ConnectionError

from PySide6.QtCore import Signal, Slot, QRunnable, QObject

from backend.core_backend import match_titles_using_db_and_format
from databases.database import Database


# pylint: disable=broad-exception-caught
class DatabaseWorker(QObject, QRunnable):
    """Used to perform a database match in a thread... so the UI won't stall during slow database calls."""
    database: Database

    finished = Signal(list)
    error = Signal(str)

    def __init__(self, database: Database):
        QObject.__init__(self)
        QRunnable.__init__(self)
        self.database = database

    @Slot()
    def run(self):
        try:
            titles = match_titles_using_db_and_format(self.database)
            self.finished.emit(titles)
        # Broad exception is caught here as database implementations throw different exceptions.
        # This is for the general case. Ideally, we should analyze and group all types of exceptions
        # into a DatabaseError. However, this should be fine for now.
        except Exception as e:
            self.error.emit(database_error_message(e))


def database_error_message(error: Exception) -> str:
    """Give actionable guidance without exposing request URLs or API keys."""
    if isinstance(error, Timeout):
        return "The metadata request timed out. Check your connection and try again."
    if isinstance(error, ConnectionError):
        return "Could not connect to the metadata provider. Check your connection and try again."
    if isinstance(error, HTTPError):
        status = error.response.status_code if error.response is not None else None
        if status in (401, 403):
            return "The provider rejected access. Check your API key on the APIs page, then try again."
        if status == 429:
            return "The provider's request limit was reached. Wait before trying again."
        return "The metadata provider could not complete the request. Try again later."
    if isinstance(error, (ValueError, KeyError, IndexError, TypeError)):
        return "Could not process the metadata or filename format. Check your format and input files, then try again."
    return "Matching failed. Try again or choose another metadata provider."
