import ctypes
import errno
import os
import sys

from backend.formats_backend import retrieve_series_format_from_formats_file, retrieve_movies_format_from_formats_file
from backend.media_record import MediaRecord
from databases.database import Database


def create_formatted_title(format_template: str, context: dict) -> str:
    """
    Create a formatted title by substituting context values into a format_template string.
    Context values correspond to the supported syntax labeled in the 'Formats' page.
    """

    # Missing values are replaced with {None} so the file name can be marked by the UI.
    normalized_context = {key: ("{None}" if value is None else value) for key, value in context.items()}

    # Returns a formatted title. The context is passed as keyword arguments,
    # instead of positional arguments, to accurately map the context info to the format.
    return format_template.format(**normalized_context)


def match_titles_using_db_and_format(database: Database) -> list[str]:
    """Match each MediaRecord in the database with a correctly formatted file name using the database."""

    formatted_titles = []
    media_records: list[MediaRecord] = database.media_records

    matched_titles = database.retrieve_media_titles_from_db()
    matched_years = database.retrieve_media_years_from_db()

    format_template = (retrieve_series_format_from_formats_file() if database.is_tv_series
                       else retrieve_movies_format_from_formats_file())

    for i, media_record in enumerate(media_records):
        if database.is_tv_series:
            # Unformatted numbers.
            raw_season_number = media_record.metadata.get("season", 1)
            raw_episode_number: int | str | None = None

            # guessit might return a list of episode numbers, e.g., S01E10-E11. Just pick the first episode.
            raw_episode_metadata = media_record.metadata.get("episode")

            if isinstance(raw_episode_metadata, list) and len(raw_episode_metadata) > 0:
                raw_episode_number = raw_episode_metadata[0]
            else:
                raw_episode_number = raw_episode_metadata

            series_context: dict = {
                "series_name": media_record.title,
                "year": matched_years[i],
                "season_number": f"{int(raw_season_number):02d}" if raw_season_number is not None else None,
                "episode_number": f"{int(raw_episode_number):02d}" if raw_episode_number is not None else None,
                "episode_title": matched_titles[i]
            }

            formatted_title = create_formatted_title(format_template, series_context)
            formatted_titles.append(f"{formatted_title}.{media_record.container}")
        else:
            movie_context: dict = {
                "movie_name": matched_titles[i],
                "year": matched_years[i]
            }

            formatted_title = create_formatted_title(format_template, movie_context)
            formatted_titles.append(f"{formatted_title}.{media_record.container}")

    return formatted_titles


def get_invalid_file_names_and_fixes(file_names: list[str]) -> dict[str, str]:
    """
    Checks for invalid file names and returns a dictionary of
    {invalid_name: fix}.
    """
    forbidden_chars = set(r'\/:*?"<>|')
    invalid_files = {}

    for file_name in file_names:
        if any(ch in forbidden_chars for ch in file_name):
            # Replace forbidden characters with blanks.
            suggested_fix = ''.join('' if ch in forbidden_chars else ch for ch in file_name)
            invalid_files[file_name] = suggested_fix

    return invalid_files


def perform_file_renaming(old_file_names: list[str], new_file_names: list[str],
                          completed_renames: list[tuple[str, str]] | None = None):
    """Validate a batch and record each completed (destination, source) pair.

    Existing destinations, including other sources in the batch, are rejected.
    Destinations are reserved atomically without overwriting another file.
    """
    if len(old_file_names) != len(new_file_names):
        raise ValueError(f"Old_file_names[] has {len(old_file_names)} files but,"
                         f"new_file_names has {len(new_file_names)} files...?")

    def path_key(path):
        return os.path.normcase(os.path.abspath(path))

    sources = [path_key(path) for path in old_file_names]
    targets = [path_key(path) for path in new_file_names]
    if len(set(sources)) != len(sources) or len(set(targets)) != len(targets):
        raise ValueError("Duplicate source or destination paths in rename batch")

    for source, target, source_key, target_key in zip(old_file_names, new_file_names, sources, targets):
        if not os.path.isfile(source):
            raise FileNotFoundError(source)
        if source_key != target_key and os.path.lexists(target):
            raise FileExistsError(f"Destination already exists: {target}")

    for source, target, source_key, target_key in zip(old_file_names, new_file_names, sources, targets):
        if source_key == target_key:
            if os.name == "nt" and os.path.abspath(source) != os.path.abspath(target):
                # Case-only renames refer to the same Windows path.
                os.rename(source, target)
                if completed_renames is not None:
                    completed_renames.append((target, source))
            continue
        _rename_without_overwrite(source, target)
        if completed_renames is not None:
            completed_renames.append((target, source))


def _rename_without_overwrite(source: str, target: str):
    """Use native no-replace renaming where available, with a safe file fallback."""
    if os.name == "nt":
        # Windows rename already rejects existing destinations.
        os.rename(source, target)
        return
    if sys.platform.startswith("linux"):
        renameat2 = getattr(ctypes.CDLL(None, use_errno=True), "renameat2", None)
        if renameat2 is not None:
            renameat2.argtypes = [ctypes.c_int, ctypes.c_char_p, ctypes.c_int, ctypes.c_char_p, ctypes.c_uint]
            renameat2.restype = ctypes.c_int
            if renameat2(-100, os.fsencode(source), -100, os.fsencode(target), 1) == 0:
                return
            error = ctypes.get_errno()
            if error not in (errno.ENOSYS, errno.EINVAL, errno.EOPNOTSUPP):
                raise OSError(error, os.strerror(error), target)
    # link() fails atomically if a destination appears after validation.
    # Filesystems without hard links fail safely instead of risking replacement.
    os.link(source, target, follow_symlinks=False)
    try:
        os.unlink(source)
    except OSError:
        os.unlink(target)
        raise
