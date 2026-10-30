from PySide6.QtWidgets import QLabel

from pages.formats import FormatsPage


def test_invalid_format_shows_feedback_without_saving(qtbot, monkeypatch):
    feedback = QLabel()
    qtbot.addWidget(feedback)
    saved = []
    monkeypatch.setattr('pages.formats.save_new_movies_format_to_formats_file', saved.append)
    FormatsPage.save_format('{missing}', False, feedback)
    assert 'Unknown placeholder' in feedback.text()
    assert saved == []
    FormatsPage.save_format('{movie_name} ({year})', False, feedback)
    assert saved == ['{movie_name} ({year})']
    assert 'Interstellar (2014).mkv' in feedback.text()
