"""Fluent-inspired colors and scalable outline icons, without extra dependencies."""
from PySide6.QtCore import QByteArray, Qt
from PySide6.QtGui import QColor, QIcon, QPainter, QPalette, QPixmap
from PySide6.QtSvg import QSvgRenderer

PATHS = {
    'menu': '<path d="M4 6h16M4 12h16M4 18h16"/>',
    'rename': '<path d="M14 3H6a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h7M14 3v6h6M14 3l6 6M12 18l7-7 3 3-7 7-4 1z"/>',
    'formats': '<rect x="5" y="3" width="14" height="18" rx="2"/><path d="M8 8h8M8 12h5M8 16h8"/>',
    'metadata': '<ellipse cx="12" cy="5" rx="8" ry="3"/><path d="M4 5v14c0 4 16 4 16 0V5M4 12c0 4 16 4 16 0"/>',
    'settings': '<path d="m10 3-1 3-3 1-3 3 2 2-1 3 2 3 3-1 3 2 3-1 1-3 3-1 1-4-3-1-1-3-3-1z"/><circle cx="12" cy="12" r="3"/>',
    'folder': '<path d="M3 7V5a2 2 0 0 1 2-2h5l2 3h7a2 2 0 0 1 2 2v11a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V7h18"/>',
    'add': '<path d="M12 4v16M4 12h16"/>',
    'remove': '<path d="M4 6h16M9 6V3h6v3M6 6l1 15h10l1-15M10 10v7M14 10v7"/>',
    'clear': '<path d="m15 3 3 2-5 8M9 10l7 4-5 8-8-4zM5 15l8 4"/>',
    'undo': '<path d="M8 4 3 9l5 5M3 9h12a6 6 0 0 1 0 12h-4"/>',
    'match': '<path d="m4 20 12-12 3 3L7 23zM5 3v5M2.5 5.5h5M18 1v5M15.5 3.5h5M20 16v5M17.5 18.5h5"/>',
    'check': '<path d="m5 12 4 4 10-10"/>',
    'warning': '<path d="m12 3 10 18H2zM12 9v5M12 17v.3"/>',
    'file': '<path d="M14 3H6a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V9zM14 3v6h6M8 13h8M8 17h6"/>',
}


def icon(name: str, color: str = '#42474f', size: int = 20) -> QIcon:
    svg = f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="{color}" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round">{PATHS[name]}</svg>'
    pixmap = QPixmap(size * 2, size * 2)
    pixmap.fill(Qt.GlobalColor.transparent)
    painter = QPainter(pixmap)
    QSvgRenderer(QByteArray(svg.encode())).render(painter)
    painter.end()
    pixmap.setDevicePixelRatio(2)
    return QIcon(pixmap)


def apply_theme(application, theme: str):
    dark = theme == 'Dark'
    colors = {
        QPalette.ColorRole.Window: '#20242b' if dark else '#f5f7fb',
        QPalette.ColorRole.Base: '#292d35' if dark else '#ffffff',
        QPalette.ColorRole.AlternateBase: '#252a32' if dark else '#fafbfc',
        QPalette.ColorRole.WindowText: '#f4f5f7' if dark else '#1b1b1b',
        QPalette.ColorRole.Text: '#f4f5f7' if dark else '#1b1b1b',
        QPalette.ColorRole.Button: '#30353e' if dark else '#ffffff',
        QPalette.ColorRole.ButtonText: '#f4f5f7' if dark else '#242424',
        QPalette.ColorRole.Mid: '#454c58' if dark else '#dce0e6',
        QPalette.ColorRole.PlaceholderText: '#aab3c2' if dark else '#657080',
        QPalette.ColorRole.Highlight: '#174e7c' if dark else '#e7f1fc',
        QPalette.ColorRole.HighlightedText: '#ffffff' if dark else '#1b1b1b',
        QPalette.ColorRole.Link: '#71b7ff' if dark else '#0067c0',
        QPalette.ColorRole.ToolTipBase: '#30353e' if dark else '#ffffff',
        QPalette.ColorRole.ToolTipText: '#ffffff' if dark else '#1b1b1b',
    }
    palette = QPalette()
    for role, color in colors.items():
        palette.setColor(role, QColor(color))
    for role in (QPalette.ColorRole.Text, QPalette.ColorRole.ButtonText, QPalette.ColorRole.WindowText):
        palette.setColor(QPalette.ColorGroup.Disabled, role, QColor('#89919b'))
    stylesheet = application.styleSheet()
    application.setStyleSheet('')
    application.setPalette(palette)
    if stylesheet:
        application.setStyleSheet(stylesheet)


def native_backdrop(window, dark: bool):
    """Let Windows supply the title bar and Windows 11's Mica backdrop when available."""
    import sys
    if sys.platform != 'win32':
        return
    import ctypes
    from ctypes import wintypes
    dwm = ctypes.WinDLL('dwmapi')
    dwm.DwmSetWindowAttribute.argtypes = [wintypes.HWND, wintypes.DWORD, wintypes.LPCVOID, wintypes.DWORD]
    dwm.DwmSetWindowAttribute.restype = ctypes.c_long
    hwnd = int(window.winId())
    for attribute, value in ((20, int(dark)), (38, 2)):
        setting = ctypes.c_int(value)
        dwm.DwmSetWindowAttribute(hwnd, attribute, ctypes.byref(setting), ctypes.sizeof(setting))
