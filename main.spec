# Build on the target OS. Produces a self-contained one-directory application.
from pathlib import Path
from PyInstaller.utils.hooks import collect_all

root = Path(SPECPATH)
datas = [(str(root / 'styles'), 'styles'), (str(root / 'resources'), 'resources')]
binaries = []
hiddenimports = []
for package in ('babelfish', 'guessit'):
    package_data, package_binaries, package_imports = collect_all(
        package,
        filter_submodules=lambda name: not any(part in {'test', 'tests'} for part in name.split('.')),
        exclude_datas=['**/test/**', '**/tests/**'],
    )
    datas += package_data
    binaries += package_binaries
    hiddenimports += package_imports

a = Analysis(
    [str(root / 'main.py')],
    pathex=[str(root)],
    binaries=binaries,
    datas=datas,
    hiddenimports=hiddenimports,
    excludes=['PySide6.QtWebEngineCore', 'PySide6.QtWebEngineWidgets',
              'PySide6.Qt3DCore', 'PySide6.Qt3DRender', 'PySide6.QtCharts',
              'PySide6.QtDataVisualization', 'PySide6.QtBluetooth', 'PySide6.QtMultimedia',
              'PySide6.QtQuick'],
    noarchive=False,
    optimize=0,
)
pyz = PYZ(a.pure)
exe = EXE(
    pyz, a.scripts, [],
    exclude_binaries=True,
    name='Simpler-FileBot',
    icon=str(root / 'resources' / 'Alternative App Logo.png'),
    debug=False,
    strip=False,
    upx=False,
    console=False,
    disable_windowed_traceback=False,
)
coll = COLLECT(exe, a.binaries, a.datas, strip=False, upx=False, name='Simpler-FileBot')
