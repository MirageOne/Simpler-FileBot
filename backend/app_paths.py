"""Application storage for installed and portable builds."""
import sys
from pathlib import Path

from platformdirs import user_data_dir


def application_data_dir() -> Path:
    if getattr(sys, 'frozen', False):
        executable_dir = Path(sys.executable).resolve().parent
        if (executable_dir / 'portable.flag').is_file():
            return executable_dir / 'data'
    return Path(user_data_dir(appauthor=False, appname='Simpler FileBot'))
