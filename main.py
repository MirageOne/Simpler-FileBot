import os
import sys
from pathlib import Path

from PySide6.QtCore import Qt, QSize, QTimer
from PySide6.QtGui import QFont, QIcon, QPixmap
from PySide6.QtWidgets import (QApplication, QMainWindow, QWidget, QHBoxLayout, QVBoxLayout,
                               QListWidget, QListWidgetItem, QStackedWidget, QPushButton, QLabel)

from pages.core.main_page import MainPage
from pages.formats import FormatsPage
from pages.settings import SettingsPage
from pages.apis import ApisPage
from backend.settings_backend import retrieve_theme_from_settings
from backend.utils import resource_path
from ui.fluent import apply_theme, native_backdrop, icon


def page_container(title, subtitle, content):
    page = QWidget()
    layout = QVBoxLayout(page)
    layout.setContentsMargins(30, 24, 30, 24)
    layout.setSpacing(12)
    heading = QLabel(title)
    heading.setObjectName('pageTitle')
    detail = QLabel(subtitle)
    detail.setObjectName('secondaryText')
    layout.addWidget(heading)
    layout.addWidget(detail)
    layout.addSpacing(8)
    layout.addWidget(content, 1)
    return page


class MainWindow(QMainWindow):
    """Native system frame around a Fluent-style navigation shell."""
    def __init__(self):
        super().__init__()
        theme = retrieve_theme_from_settings()
        apply_theme(QApplication.instance(), theme)
        self.setWindowTitle('Simpler FileBot')
        self.setWindowIcon(QIcon(QPixmap(resource_path('resources/Alternative App Logo.png'))))
        self.resize(1380, 900)
        self.setMinimumSize(1040, 720)
        shell = QWidget()
        shell_layout = QHBoxLayout(shell)
        shell_layout.setContentsMargins(0, 0, 0, 0)
        shell_layout.setSpacing(0)
        self.sidebar = QWidget()
        self.sidebar.setObjectName('navigationPane')
        self.sidebar.setFixedWidth(208)
        navigation = QVBoxLayout(self.sidebar)
        navigation.setContentsMargins(8, 14, 8, 14)
        navigation.setSpacing(12)
        self.menu_toggle = QPushButton()
        self.menu_toggle.setIcon(icon('menu', '#748093'))
        self.menu_toggle.setObjectName('subtleButton')
        self.menu_toggle.setFixedSize(40, 36)
        self.menu_toggle.setToolTip('Collapse navigation')
        self.menu_toggle.setAccessibleName('Toggle navigation')
        self.menu_toggle.clicked.connect(self.toggle_navigation)
        navigation.addWidget(self.menu_toggle)
        self.menu = QListWidget()
        self.menu.setObjectName('menu')
        self.menu.setIconSize(QSize(20, 20))
        self.menu.setHorizontalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAlwaysOff)
        self.menu.setAccessibleName('Main navigation')
        for text, glyph in (('Rename', 'rename'), ('Formats', 'formats'), ('Metadata', 'metadata')):
            item = QListWidgetItem(icon(glyph, '#748093'), text)
            item.setSizeHint(QSize(184, 46))
            item.setToolTip(text)
            self.menu.addItem(item)
        navigation.addWidget(self.menu, 1)
        self.settings_button = QPushButton('Settings')
        self.settings_button.setIcon(icon('settings', '#748093'))
        self.settings_button.setObjectName('navigationSettings')
        self.settings_button.setCheckable(True)
        self.settings_button.clicked.connect(self.open_settings)
        navigation.addWidget(self.settings_button)
        shell_layout.addWidget(self.sidebar)
        self.pages = QStackedWidget()
        self.main_page = MainPage()
        self.formats_page = FormatsPage()
        self.pages.addWidget(self.main_page)
        self.pages.addWidget(page_container('Formats', 'Choose how your movies and TV episodes are named.', self.formats_page))
        self.pages.addWidget(page_container('Metadata', 'Connect your metadata providers. API keys stay on this device.', ApisPage()))
        self.pages.addWidget(page_container('Settings', 'Make Simpler FileBot work your way.', SettingsPage()))
        shell_layout.addWidget(self.pages, 1)
        self.setCentralWidget(shell)
        self.menu.currentRowChanged.connect(self.navigate)
        self.main_page.renamer_widget.editFormatRequested.connect(lambda: self.menu.setCurrentRow(1))
        self.menu.setCurrentRow(0)
        self._collapsed = False
        native_backdrop(self, theme == 'Dark')

    def navigate(self, row):
        if row >= 0:
            self.pages.setCurrentIndex(row)
            self.settings_button.setChecked(False)

    def open_settings(self):
        self.menu.setCurrentRow(-1)
        self.pages.setCurrentIndex(3)
        self.settings_button.setChecked(True)

    def toggle_navigation(self):
        self._collapsed = not self._collapsed
        self.sidebar.setFixedWidth(64 if self._collapsed else 208)
        for row, text in enumerate(('Rename', 'Formats', 'Metadata')):
            self.menu.item(row).setText('' if self._collapsed else text)
        self.settings_button.setText('' if self._collapsed else 'Settings')
        self.settings_button.setToolTip('Settings')
        self.menu_toggle.setToolTip('Expand navigation' if self._collapsed else 'Collapse navigation')


def apply_stylesheet(application: QApplication, qss_path: str):
    application.setStyle('Fusion')
    application.setFont(QFont('Segoe UI', 10))
    with Path(qss_path).open('r', encoding='utf-8') as stylesheet:
        contents = stylesheet.read()
        for filename in ('check.svg', 'chevron.svg'):
            path = resource_path('resources/' + filename).replace('\\', '/')
            contents = contents.replace(f'url(resources/{filename})', f'url("{path}")')
        application.setStyleSheet(contents)


if __name__ == '__main__':
    app = QApplication(sys.argv)
    apply_stylesheet(app, resource_path('styles/default.qss'))
    main_window = MainWindow()
    main_window.show()
    dropped_paths = [path for path in sys.argv[1:] if os.path.exists(path)]
    if dropped_paths:
        main_window.main_page.add_files(dropped_paths)
    if os.environ.get('CI', 'false').lower() == 'true':
        QTimer.singleShot(1000, app.quit)
    sys.exit(app.exec())
