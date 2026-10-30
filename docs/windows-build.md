# Build Simpler FileBot on Windows

Use Windows 10 or 11 x64. Build on Windows: PyInstaller cannot produce a Windows executable from Linux. These steps produce a **portable ZIP** and an **Inno Setup installer** from the same application bundle. Use PowerShell from the repository root (the folder containing `main.py`, `main.spec`, and both requirements files).

## 1. Install the build tools

Install Git and **Python 3.12 x64**. The app supports other Python versions, but the build script and Windows CI use 3.12 x64. If Python 3.12 is missing:

```powershell
winget install --id Python.Python.3.12 --exact
```

Close and reopen PowerShell after installation. Check the version:

```powershell
py -3.12 --version
```

If `py` is **not recognized**, the Python launcher is missing or not on `PATH`. Locate Python directly:

```powershell
$FileBotPython = "$env:LOCALAPPDATA\Programs\Python\Python312\python.exe"
if (-not (Test-Path $FileBotPython)) {
    $FileBotPython = "$env:ProgramFiles\Python312\python.exe"
}
& $FileBotPython --version
```

The version must report **Python 3.12.x**. If neither path exists, find your Python 3.12 installation and set `$FileBotPython` to its full `python.exe` path. For example, `Get-Command python3.12 -ErrorAction SilentlyContinue` may find another installation. A Microsoft Store `python` alias alone does not establish which Python version it starts.

For an **installer** build, install [Inno Setup](https://jrsoftware.org/isinfo.php) 6.3 or newer. Portable builds do not need it.

```powershell
winget install --id JRSoftware.InnoSetup --exact
```

If Winget says Inno Setup is already installed, continue. Its compiler may be in a per-user folder rather than `Program Files`; see [Compiler not found](#compiler-not-found) below if the script cannot locate it.

## 2. Prepare the repository and virtual environment

If you already cloned the repo, change to that checkout and pull the latest `main`:

```powershell
cd "C:\path\to\Simpler-FileBot"
git pull origin main
```

For a fresh checkout instead:

```powershell
git clone https://github.com/MirageOne/Simpler-FileBot.git
cd Simpler-FileBot
```

Verify you are in the repository root:

```powershell
Test-Path .\main.spec
Test-Path .\requirements-build.txt
```

Both commands should return `True`. Create the virtual environment with **one** of these methods:

```powershell
# If the py launcher works:
py -3.12 -m venv venv
```

```powershell
# If py is unavailable, use $FileBotPython from step 1:
& $FileBotPython -m venv venv
```

Check that the environment was created, then install runtime and build dependencies:

```powershell
.\venv\Scripts\python.exe --version
.\venv\Scripts\python.exe -m pip install -r requirements.txt -r requirements-build.txt
.\venv\Scripts\python.exe -m pip check
```

If `venv\Scripts\python.exe` is missing, the `venv` command failed; fix the Python 3.12 path before continuing. The virtual environment is ignored by Git, and you do not need to activate it.

## 3. Validate the source application

```powershell
.\venv\Scripts\python.exe -m pytest -q
.\venv\Scripts\python.exe main.py
```

The tests should finish with a passing count. In the app, use disposable media files to check adding files, local matching, renaming, and Undo; then close it. TMDB and OMDB live matching need API keys entered on the Metadata page. Local metadata matching needs no API key.

## 4. Build the packages

From the repository root, build both targets:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-windows.ps1 -Target All -Python .\venv\Scripts\python.exe
```

`-ExecutionPolicy Bypass` applies only to that PowerShell process. It does not change the machine's execution policy. The script verifies Python 3.12 x64 and the required packages, freezes the app with PyInstaller, smoke-tests the executable, and compiles the installer with Inno Setup.

Build one target when needed:

```powershell
# No Inno Setup required:
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-windows.ps1 -Target Portable -Python .\venv\Scripts\python.exe

# Requires Inno Setup:
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-windows.ps1 -Target Installer -Python .\venv\Scripts\python.exe
```

The default package version is `1.6.0`; set another numeric version with `-Version 1.6.1`. `make_exe.cmd` is a convenience wrapper for the **portable** target.

## 5. Find and use the outputs

```powershell
Get-ChildItem .\dist\windows\packages
```

With the default version, the outputs are:

| Output | File | How to use it |
| --- | --- | --- |
| Portable | `Simpler-FileBot-1.6.0-portable-x64.zip` | Extract the entire ZIP into a writable folder and run `Simpler-FileBot.exe` inside it. |
| Installer | `Simpler-FileBot-1.6.0-setup-x64.exe` | Run the installer; launch from the Start menu afterward. |

The portable build is a **folder**, not a single executable. Keep `_internal` and `portable.flag` alongside the EXE. It writes settings and API keys to `data` beside the EXE. The script removes its own smoke-test data before creating the ZIP. Remove `data` before sharing a portable copy you have used.

The installer installs for the current user under `%LOCALAPPDATA%\Programs\Simpler FileBot`, creates a Start menu shortcut, and can optionally create a desktop shortcut. It can be removed from **Installed apps**. Its settings stay in your per-user application-data folder, accessible from **Settings → Open settings folder** in the app. Uninstalling preserves settings. Neither package requires Python on the destination computer.

The packages are unsigned. Public releases can be signed with your own Authenticode certificate.

## Troubleshooting

### `py` is not recognized

Use the direct Python 3.12 path in step 1 and create the virtual environment with `& $FileBotPython -m venv venv`. The later commands call `venv\Scripts\python.exe` directly, so they do not need the launcher.

### `venv\Scripts\python.exe` is not recognized

The virtual environment does not exist at this path. Check `Test-Path .\venv\Scripts\python.exe`, confirm you are in the repository root, and rerun the `venv` creation command using Python 3.12 x64. Do not proceed to dependency installation until this returns `True`.

### Compiler not found

`winget` can report Inno Setup as installed even when `ISCC.exe` is not on `PATH`. The build script checks system and per-user `Inno Setup 6` and `Inno Setup 7` folders. If it still reports **Install Inno Setup 6.3+**, locate the compiler:

```powershell
Get-ChildItem "$env:LOCALAPPDATA\Programs", "${env:ProgramFiles(x86)}", "$env:ProgramFiles" -Filter ISCC.exe -Recurse -ErrorAction SilentlyContinue |
    Select-Object -ExpandProperty FullName
```

Pass the returned full path to the build script:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-windows.ps1 -Target All -Python .\venv\Scripts\python.exe -IsccPath "C:\full\path\to\ISCC.exe"
```

If the search returns no path, Inno Setup's compiler is not installed at an accessible location. Reinstall Inno Setup from the official site or build only the portable target.

### Frozen app does not start

The build script stops if its startup check fails. Confirm that `python main.py` works in step 3, keep the full PyInstaller output in `dist\windows\Simpler-FileBot`, and run the EXE from that folder to investigate. The portable ZIP and installer use that same bundle.

## GitHub Actions

The [Windows packages workflow](../.github/workflows/windows-packages.yml) builds both packages on `windows-latest`, smoke-tests the frozen and installed apps, and uploads the ZIP and installer as workflow artifacts. It runs on relevant pushes to `main`, pull requests, and manual dispatch. It does not create a GitHub Release. The regular test workflow runs the suite on Linux and Windows, including Windows-only rename tests.
