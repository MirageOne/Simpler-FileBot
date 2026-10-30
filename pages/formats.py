import textwrap

from PySide6.QtGui import Qt
from PySide6.QtWidgets import QWidget, QVBoxLayout, QLineEdit, QPushButton, QTabWidget, QTextBrowser, QLabel

from backend.formats_backend import retrieve_movies_format_from_formats_file, save_new_movies_format_to_formats_file, \
    retrieve_series_format_from_formats_file, save_new_series_format_to_formats_file, preview_format


class FormatsPage(QWidget):
    """Formats page for users to set what renaming format they want for TV episodes and movies."""

    def __init__(self, parent=None):
        super().__init__(parent)
        self.setObjectName("formats")

        formats_page_layout = QVBoxLayout(self)

        # Tab bar for Movies & TV Episodes.
        tab_bar = QTabWidget()
        formats_page_layout.addWidget(tab_bar)

        # Movie tab.
        movie_tab = QWidget()
        movie_tab_layout = QVBoxLayout(movie_tab)

        # Input textbox for users to view and edit their desired movies renaming format.
        movie_line_edit = QLineEdit()
        movie_line_edit.setText(retrieve_movies_format_from_formats_file())
        movie_tab_layout.addWidget(movie_line_edit)

        # Update button for movies format.
        movie_update_button = QPushButton("Update")
        movie_feedback = QLabel()
        movie_feedback.setWordWrap(True)
        movie_feedback.setTextFormat(Qt.TextFormat.PlainText)
        movie_feedback.setObjectName("movieFormatFeedback")
        movie_update_button.clicked.connect(lambda: self.save_format(movie_line_edit.text(), False, movie_feedback))
        movie_tab_layout.addWidget(movie_update_button)
        movie_tab_layout.addWidget(movie_feedback)

        # Movie format tutorial and examples.
        movie_syntax = QTextBrowser()
        movie_syntax.setMarkdown(textwrap.dedent("""
        **-** {movie_name} = **Title of movie**  \n
        **-** {year} = **Year of movie**  \n
        <br/>**Examples**:
        <br/><br/>{movie_name} ({year}): ***Interstellar (2014)***
        """))
        movie_tab_layout.addWidget(movie_syntax)

        movie_tab.layout().setAlignment(Qt.AlignmentFlag.AlignTop)

        # Episode tab.
        episode_tab = QWidget()
        episode_tab_layout = QVBoxLayout(episode_tab)

        # Textbox.
        series_line_edit = QLineEdit()
        series_line_edit.setText(retrieve_series_format_from_formats_file())
        episode_tab_layout.addWidget(series_line_edit)

        # Update button for series format.
        series_update_button = QPushButton("Update")
        series_feedback = QLabel()
        series_feedback.setWordWrap(True)
        series_feedback.setTextFormat(Qt.TextFormat.PlainText)
        series_feedback.setObjectName("seriesFormatFeedback")
        series_update_button.clicked.connect(lambda: self.save_format(series_line_edit.text(), True, series_feedback))
        episode_tab_layout.addWidget(series_update_button)
        episode_tab_layout.addWidget(series_feedback)

        # Episode format tutorial and examples.
        episode_syntax = QTextBrowser()
        episode_syntax.setMarkdown(textwrap.dedent("""
        **-** {series_name} = **Name of television show**  \n
        **-** {season_number} = **Season number of an episode**  \n
        **-** {episode_number} = **Episode number of an episode**  \n
        **-** {episode_title} = **Title of an episode**  \n
        **-** {year} = **Premiere year of a television show**  \n
        <br/>**Examples**:
        <br/><br/>S{season_number}E{episode_number} - {episode_title}: ***S02E22 - Two Cathedrals***
        <br/><br/>{series_name} ({year}) - E{episode_number}: ***The West Wing (1999) - E01***
        """))
        episode_tab_layout.addWidget(episode_syntax)

        episode_tab.layout().setAlignment(Qt.AlignmentFlag.AlignTop)

        tab_bar.addTab(movie_tab, "Movies")
        tab_bar.addTab(episode_tab, "TV Episodes")


    @staticmethod
    def save_format(template: str, is_series: bool, feedback: QLabel):
        try:
            example = preview_format(template, is_series)
            save = save_new_series_format_to_formats_file if is_series else save_new_movies_format_to_formats_file
            save(template)
        except (ValueError, OSError) as error:
            feedback.setText(str(error))
            return
        feedback.setText(f"Saved. Example: {example}")
