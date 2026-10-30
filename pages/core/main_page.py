from PySide6.QtWidgets import QWidget, QVBoxLayout
from pages.core.core_renamer_widget import CoreRenamerWidget


class MainPage(QWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        layout = QVBoxLayout(self)
        layout.setContentsMargins(0, 0, 0, 0)
        self.renamer_widget = CoreRenamerWidget()
        layout.addWidget(self.renamer_widget)

    def add_files(self, paths):
        self.renamer_widget.add_files(paths)
