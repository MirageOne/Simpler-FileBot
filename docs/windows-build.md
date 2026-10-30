# Build Windows installer and portable packages

Build **on Windows 10/11 x64 with Python 3.12 x64**. PyInstaller does not cross-compile Windows applications from Linux.
The application uses PySide6 with Fluent-style widgets, the native Windows title bar, and Windows 11 backdrop support where available.

## Prerequisites

Install Git, Python 3.12 x64 (including the `py` launcher), and, for the installer, **Inno Setup 6.3 or newer**.
You can install Inno Setup with:

```powershell
winget install --id JRSoftware.InnoSetup --exact
```

Portable builds do not need Inno Setup. Use a normal PowerShell terminal; building and running the app do not require administrator rights.

## Prepare the checkout

```powershell
git clone https://github.com/MirageOne/Simpler-FileBot.git
cd Simpler-FileBot
py -3.12 -m venv venv
.\venv\Scripts\python.exe -m pip install -r requirements.txt -r requirements-build.txt
.\venv\Scripts\python.exe -m pip check
```

For an existing checkout, pull the latest `main` before building. The commands use the virtual environment directly, so activation is unnecessary.

## Validate the source application

```powershell
.\venv\Scripts\python.exe -m pytest -q
.\venv\Scripts\python.exe main.py
```

Close the app after checking navigation, adding media, matching, renaming, and Undo with disposable files.

## Build both packages

Run from the repository root:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-windows.ps1 -Target All -Python .\venv\Scripts\python.exe
```

`-ExecutionPolicy Bypass` applies only to this PowerShell process. The script does not change the machine's execution policy.
The script freezes the app, runs a startup smoke check, and builds these outputs:

- `dist\windows\packages\Simpler-FileBot-1.6.0-portable-x64.zip`
- `dist\windows\packages\Simpler-FileBot-1.6.0-setup-x64.exe`

Build one target with `-Target Portable` or `-Target Installer`. Set a numeric version with `-Version 1.6.1`.
If Inno Setup was installed in a custom location, add `-IsccPath 'C:\path\to\ISCC.exe'`.
The existing `make_exe.cmd` is a convenience wrapper for the portable target; pass `-Python .\venv\Scripts\python.exe` when using this virtual environment.

### Portable

Extract the ZIP into a writable folder and run `Simpler-FileBot.exe`. Keep the **entire** extracted folder, including `_internal` and `portable.flag`, together.
No Python installation is needed on the destination machine. This is a folder-based portable build, not a single-file executable.

Settings and API keys are written to the `data` folder beside the executable. The build never copies settings from your profile into the package;
its own temporary smoke-test settings are removed before zipping. Remove `data` before sharing a previously used portable copy.
Deleting the portable folder removes the app and its portable settings.

### Installer

Run `Simpler-FileBot-1.6.0-setup-x64.exe`. It installs for the current user under
`%LOCALAPPDATA%\Programs\Simpler FileBot`, adds a Start menu shortcut, and offers an optional desktop shortcut.
It can be removed through Windows **Installed apps**. Settings stay in the platformdirs per-user application-data directory,
which can be opened from the app's **Settings → Open settings folder**. Uninstalling preserves those settings.
The installer does not contain `portable.flag` and does not use the portable `data` directory.

The build script produces unsigned binaries. Public distribution can add Authenticode signing using your own certificate.

## GitHub Actions

The **Windows packages** workflow builds both packages on `windows-latest`, smoke-tests the frozen app and installed copy,
and uploads the ZIP and installer as workflow artifacts. It runs on relevant pushes to `main`, pull requests, and manual dispatch.
It does not create a GitHub Release or publish the cloud environment.

The regular test workflow separately runs the suite on Linux and Windows, including Windows-only case-only rename and exclusive file-lock tests.
