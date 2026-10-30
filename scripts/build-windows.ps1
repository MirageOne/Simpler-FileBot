[CmdletBinding()]
param(
    [ValidateSet('All', 'Portable', 'Installer')]
    [string]$Target = 'All',
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '1.6.0',
    [string]$Python = 'python',
    [string]$IsccPath = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'Run this script on Windows x64.' }
$Root = Split-Path $PSScriptRoot -Parent
$Dist = Join-Path $Root 'dist\windows'
$Build = Join-Path $Root 'build\windows'
$Packages = Join-Path $Dist 'packages'
$App = Join-Path $Dist 'Simpler-FileBot'

# Resolve required prerequisites before spending time freezing the application.
& $Python -c "import sys, struct; assert sys.version_info[:2] == (3, 12), 'Use Python 3.12'; assert struct.calcsize('P') == 8, 'Use 64-bit Python'; import PyInstaller, PIL, PySide6"
if ($LASTEXITCODE -ne 0) { throw 'Install Python 3.12 x64 and both requirements files first.' }
if ($Target -ne 'Portable') {
    if (-not $IsccPath) {
        $Compiler = Get-Command ISCC.exe -ErrorAction SilentlyContinue
        if ($Compiler) { $IsccPath = $Compiler.Source }
        # Winget can install Inno Setup for the current user, outside Program Files.
        $ProgramRoots = @(${env:ProgramFiles(x86)}, $env:ProgramFiles)
        if ($env:LOCALAPPDATA) { $ProgramRoots += Join-Path $env:LOCALAPPDATA 'Programs' }
        foreach ($ProgramRoot in $ProgramRoots) {
            foreach ($Release in @('Inno Setup 6', 'Inno Setup 7')) {
                if ($ProgramRoot -and -not $IsccPath) {
                    $Candidate = Join-Path $ProgramRoot "$Release\ISCC.exe"
                    if (Test-Path -LiteralPath $Candidate) { $IsccPath = $Candidate }
                }
            }
        }
    }
    if (-not $IsccPath -or -not (Test-Path -LiteralPath $IsccPath)) {
        throw 'Install Inno Setup 6.3+ or pass -IsccPath to ISCC.exe. Portable builds do not need Inno Setup.'
    }
}

function Test-FrozenApplication([string]$Directory) {
    $PreviousCI = $env:CI
    $PreviousQt = $env:QT_QPA_PLATFORM
    $Process = $null
    try {
        $env:CI = 'true'
        $env:QT_QPA_PLATFORM = 'offscreen'
        $Process = Start-Process -FilePath (Join-Path $Directory 'Simpler-FileBot.exe') -WorkingDirectory $Directory -PassThru
        if (-not $Process.WaitForExit(60000)) {
            $Process.Kill()
            throw 'Frozen application startup timed out.'
        }
        if ($Process.ExitCode -ne 0) { throw "Frozen application failed with exit code $($Process.ExitCode)." }
    } finally {
        $env:CI = $PreviousCI
        $env:QT_QPA_PLATFORM = $PreviousQt
        if ($Process) { $Process.Dispose() }
    }
}

Push-Location $Root
try {
    & $Python -m PyInstaller --noconfirm --clean --distpath $Dist --workpath $Build (Join-Path $Root 'main.spec')
    if ($LASTEXITCODE -ne 0) { throw 'PyInstaller failed.' }
    if (-not (Test-Path (Join-Path $App 'Simpler-FileBot.exe'))) { throw 'Application executable was not produced.' }
    New-Item -ItemType Directory -Force -Path $Packages | Out-Null
    if ($Target -in @('All', 'Portable')) {
        $Name = "Simpler-FileBot-$Version-portable-x64"
        $StageParent = Join-Path $Build 'portable'
        $Stage = Join-Path $StageParent $Name
        if (Test-Path -LiteralPath $Stage) { Remove-Item -LiteralPath $Stage -Recurse -Force }
        New-Item -ItemType Directory -Force -Path $Stage | Out-Null
        Copy-Item -Path (Join-Path $App '*') -Destination $Stage -Recurse
        Copy-Item -LiteralPath (Join-Path $Root 'LICENSE') -Destination $Stage
        Set-Content -LiteralPath (Join-Path $Stage 'portable.flag') -Value '' -Encoding ASCII
        Set-Content -LiteralPath (Join-Path $Stage 'README-portable.txt') -Encoding UTF8 -Value @'
Run Simpler-FileBot.exe. Keep the entire folder, including _internal, together.
No Python installation or administrator rights are required to run the app.
Settings and API keys are saved in the data folder beside the executable.
Use a writable folder. Remove the data folder before sharing a copy with anyone.
To uninstall the portable app, delete this folder after preserving any wanted settings.
'@
        Test-FrozenApplication $Stage
        # Smoke-test data is generated in this fresh staging directory, never copied from a user profile.
        $SmokeData = Join-Path $Stage 'data'
        if (Test-Path -LiteralPath $SmokeData) { Remove-Item -LiteralPath $SmokeData -Recurse -Force }
        Compress-Archive -LiteralPath $Stage -DestinationPath (Join-Path $Packages "$Name.zip") -Force
    }
    if ($Target -in @('All', 'Installer')) {
        Test-FrozenApplication $App
        & $IsccPath "/DAppVersion=$Version" "/DAppSourceDir=$App" "/DRepoDir=$Root" "/DPackageDir=$Packages" (Join-Path $Root 'packaging\windows\installer.iss')
        if ($LASTEXITCODE -ne 0) { throw 'Inno Setup failed.' }
        if (-not (Test-Path (Join-Path $Packages "Simpler-FileBot-$Version-setup-x64.exe"))) {
            throw 'The installer was not produced.'
        }
    }
    Write-Host "Build complete. Packages: $Packages"
} finally {
    Pop-Location
}
