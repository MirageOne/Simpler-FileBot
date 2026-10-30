from main import MainWindow


def test_navigation_and_format_link(qtbot):
    window = MainWindow()
    qtbot.addWidget(window)
    window.main_page.renamer_widget.edit_format.click()
    assert window.pages.currentIndex() == 1
    window.open_settings()
    assert window.pages.currentIndex() == 3
    assert window.settings_button.isChecked()
    window.menu.setCurrentRow(2)
    assert window.pages.currentIndex() == 2
    assert not window.settings_button.isChecked()
    window.toggle_navigation()
    assert window.sidebar.width() == 64
    window.toggle_navigation()
    assert window.menu.item(0).text() == 'Rename'
