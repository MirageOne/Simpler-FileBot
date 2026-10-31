# Build the native WinUI 3 app on Windows

Use Windows 10 version 1809 or newer, or Windows 11, on an x64 PC. Build on Windows; WinUI 3 XAML compilation and the Windows installer are not supported on Linux. Run these commands in PowerShell from the repository root, the folder containing `src`, `tests`, and `scripts`.

## 1. Install build tools

Install the **.NET 10 SDK** (x64), then reopen PowerShell. `global.json` pins the solution to the latest installed .NET 10 SDK feature band:

```powershell
winget install --id Microsoft.DotNet.SDK.10 --exact
dotnet --version
```

Install Visual Studio 2026 with the **Windows application development** workload, including the Windows SDK and Windows App SDK tooling. Visual Studio Community is sufficient. The project restores Windows App SDK 1.8 from NuGet. If you already have Visual Studio, use Visual Studio Installer → **Modify** to add the workload.

For an installer build, install Inno Setup 6.3 or newer. A portable build does not need it:

```powershell
winget install --id JRSoftware.InnoSetup --exact
```

## 2. Get the main branch and test it

For a fresh checkout:

```powershell
git clone https://github.com/MirageOne/Simpler-FileBot.git
cd Simpler-FileBot
git switch main
```

In an existing checkout, update the branch before building:

```powershell
git fetch origin
git switch main
git pull --ff-only origin main
```

Confirm you are at the root:

```powershell
Test-Path .\src\SimplerFileBot.WinUI\SimplerFileBot.WinUI.csproj
dotnet test .\tests\SimplerFileBot.Core.Tests\SimplerFileBot.Core.Tests.csproj -c Release
```

`Test-Path` should return `True`; the tests should pass. No `py` launcher, Python virtual environment, or pip packages are needed.

To run the native app from source:

```powershell
dotnet run --project .\src\SimplerFileBot.WinUI\SimplerFileBot.WinUI.csproj -c Debug -r win-x64
```

Add disposable media files and check matching, preview edits, renaming, and undo. TMDB and OMDB calls need keys; TVMaze and local matching do not.

## 3. Build portable and installer packages

Build both packages from the repository root. `Test-Path .\scripts\build-windows.ps1` must return `True` before running the build command:

```powershell
Test-Path .\scripts\build-windows.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-windows.ps1 -Target All
```

Build just the portable EXE, or request the installer as well:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-windows.ps1 -Target Portable
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-windows.ps1 -Target Installer
```

The script runs core tests, publishes a self-contained `win-x64` .NET and Windows App SDK app, and always creates the single-file portable EXE. `-Target All` (the default) and `-Target Installer` also create the installer. If Inno Setup is unavailable, the portable EXE is still built before the installer step reports the missing prerequisite. Use `-Version 2.1.6` to change the package version. Use `-DotNet "C:\full\path\to\dotnet.exe"` if `dotnet` is not on `PATH`.

Outputs are placed in `dist\windows\packages`:

| Package | Default filename | Use |
| --- | --- | --- |
| Portable | `Simpler-FileBot-2.1.5-portable-x64.exe` | Put this single file in a writable folder and run it directly. |
| Installer | `Simpler-FileBot-2.1.5-setup-x64.exe` | Run the installer, then launch from the Start menu. |

The portable output in `dist\windows\packages` is one EXE. The build removes portable ZIPs left there by older builds and cleans its temporary `build\windows\portable` staging folder. The EXE creates a `data` folder beside itself for settings, formats, and API keys; remove `data` before sharing a portable copy you used. Startup can take longer while it unpacks its native runtime into a temporary folder; that folder is cleaned after the app closes. The installer defaults to `%LOCALAPPDATA%\Programs\Simpler FileBot` and stores user data in `%LOCALAPPDATA%\Simpler FileBot`. The native app reads the existing Python edition JSON files from that location. Both packages include the required .NET runtime and Windows App SDK components; users need no Python or separate .NET install.

The packages are unsigned. Windows may show a SmartScreen warning until you sign a release with your own Authenticode certificate.

## Troubleshooting

If you still see a `*-portable-x64.zip`, it came from an older build script. Update the `main` branch, run the build again, and use `dist\windows\packages\Simpler-FileBot-2.1.5-portable-x64.exe`. The new script removes legacy portable ZIPs from that package folder after the EXE is built. An older ZIP elsewhere, such as Downloads, must be deleted separately.

If `dotnet` is not recognized, close and reopen PowerShell after installing the SDK. Confirm `dotnet --version` reports 10.x. If it reports another version, install .NET 10 alongside it or pass the full SDK `dotnet.exe` path using `-DotNet`.

If a Windows App SDK or XAML build target is missing, open Visual Studio Installer and add the Windows application development workload and current Windows SDK. Then rerun `dotnet restore .\src\SimplerFileBot.WinUI\SimplerFileBot.WinUI.csproj`.

If the portable executable closes without opening a window, rebuild from the latest `main` branch and run the new `Simpler-FileBot-2.1.5-portable-x64.exe` from a writable folder. The startup check can be run on Windows with `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\smoke-test-windows.ps1 -Executable .\dist\windows\packages\Simpler-FileBot-2.1.5-portable-x64.exe`. If startup still fails, check `data\launcher-error.log` beside the portable EXE, `%LOCALAPPDATA%\Simpler FileBot\startup-error.log`, and Windows Event Viewer → Windows Logs → Application.

If Inno Setup is installed but the script cannot find `ISCC.exe`, locate it and pass its full path:

```powershell
Get-ChildItem "$env:LOCALAPPDATA\Programs", "${env:ProgramFiles(x86)}", "$env:ProgramFiles" -Filter ISCC.exe -Recurse -ErrorAction SilentlyContinue |
    Select-Object -ExpandProperty FullName
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-windows.ps1 -Target All -IsccPath "C:\full\path\to\ISCC.exe"
```

You can always build just the portable package without Inno Setup.

The [Windows packages workflow](../.github/workflows/windows-packages.yml) builds and uploads the portable EXE first on every push to `main` and every pull request. It then builds and uploads the installer as a separate artifact. The [.NET test workflow](../.github/workflows/test.yml) runs core tests on Linux and Windows.
