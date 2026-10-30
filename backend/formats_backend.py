from string import Formatter

from backend.json_config import JSONConfig

formats_json_config = JSONConfig(
    "formats.json",
    {
        "movie_format": "{movie_name} ({year})",
        "series_format": "S{season_number}E{episode_number} - {episode_title}"
    }
)


def retrieve_movies_format_from_formats_file():
    return formats_json_config.get("movie_format", "{movie_name} ({year})")


def save_new_movies_format_to_formats_file(new_movie_format: str):
    preview_format(new_movie_format, False)
    formats_json_config.set("movie_format", new_movie_format)


def retrieve_series_format_from_formats_file():
    return formats_json_config.get("series_format", "S{season_number}E{episode_number} - {episode_title}")


def save_new_series_format_to_formats_file(new_series_format: str):
    preview_format(new_series_format, True)
    formats_json_config.set("series_format", new_series_format)


MOVIE_EXAMPLE = {"movie_name": "Interstellar", "year": 2014}
SERIES_EXAMPLE = {"series_name": "The West Wing", "year": 1999,
                  "season_number": "02", "episode_number": "22", "episode_title": "Two Cathedrals"}


def preview_format(template: str, is_series: bool) -> str:
    """Validate documented placeholders and return a representative filename."""
    context = SERIES_EXAMPLE if is_series else MOVIE_EXAMPLE
    try:
        for _, field, spec, _ in Formatter().parse(template):
            if field is not None and field not in context:
                raise ValueError(f"Unknown placeholder {{{field}}}. Supported: " + ", ".join(context))
            if spec and ("{" in spec or "}" in spec):
                raise ValueError("Nested placeholders in format specifications are not supported.")
        result = template.format(**context)
        if not result.strip():
            raise ValueError("The format must produce a nonempty filename.")
        return result + ".mkv"
    except (KeyError, IndexError, TypeError, ValueError) as error:
        raise ValueError(f"Invalid format: {error}") from error
