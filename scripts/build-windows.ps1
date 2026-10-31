[CmdletBinding()]
param(
    [ValidateSet('All', 'Portable', 'Installer')]
    [string]$Target = 'All',
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '2.1.5',
    [string]$DotNet = 'dotnet',
    [string]$IsccPath = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'Run this script on Windows x64.' }
$Root = Split-Path $PSScriptRoot -Parent
$Project = Join-Path $Root 'src\SimplerFileBot.WinUI\SimplerFileBot.WinUI.csproj'
$LauncherProject = Join-Path $Root 'src\SimplerFileBot.PortableLauncher\SimplerFileBot.PortableLauncher.csproj'
$Tests = Join-Path $Root 'tests\SimplerFileBot.Core.Tests\SimplerFileBot.Core.Tests.csproj'
$Dist = Join-Path $Root 'dist\windows'
$Build = Join-Path $Root 'build\windows'
$Packages = Join-Path $Dist 'packages'
$App = Join-Path $Build 'app'

& $DotNet --version
if ($LASTEXITCODE -ne 0) { throw 'Install .NET SDK 10 x64 and make dotnet available on PATH.' }

Push-Location $Root
try {
    & $DotNet test $Tests -c Release
    if ($LASTEXITCODE -ne 0) { throw '.NET tests failed.' }
    $PreviousPublish = Join-Path $Dist 'Simpler-FileBot'
    if (Test-Path -LiteralPath $PreviousPublish) { Remove-Item -LiteralPath $PreviousPublish -Recurse -Force }
    if (Test-Path -LiteralPath $App) { Remove-Item -LiteralPath $App -Recurse -Force }
    & $DotNet publish $Project -c Release -r win-x64 --self-contained true `
        -p:WindowsAppSDKSelfContained=true "-p:Version=$Version" -o $App
    if ($LASTEXITCODE -ne 0) { throw 'WinUI 3 publish failed.' }
    $Executable = Join-Path $App 'Simpler-FileBot.exe'
    if (-not (Test-Path -LiteralPath $Executable)) { throw 'Application executable was not produced.' }
    New-Item -ItemType Directory -Force -Path $Packages | Out-Null

    $Name = "Simpler-FileBot-$Version-portable-x64"
    $Stage = Join-Path (Join-Path $Build 'portable') $Name
    $Payload = Join-Path (Join-Path $Build 'portable') 'payload.zip'
    $LauncherOutput = Join-Path (Join-Path $Build 'portable') 'launcher'
    if (Test-Path -LiteralPath $Stage) { Remove-Item -LiteralPath $Stage -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $Stage | Out-Null
    Copy-Item -Path (Join-Path $App '*') -Destination $Stage -Recurse
    Copy-Item -LiteralPath (Join-Path $Root 'LICENSE') -Destination $Stage
    Set-Content -LiteralPath (Join-Path $Stage 'portable.flag') -Value '' -Encoding ASCII
    Compress-Archive -Path (Join-Path $Stage '*') -DestinationPath $Payload -Force
    if (Test-Path -LiteralPath $LauncherOutput) { Remove-Item -LiteralPath $LauncherOutput -Recurse -Force }
    & $DotNet publish $LauncherProject -c Release -r win-x64 --self-contained true `
        "-p:PayloadZip=$Payload" "-p:Version=$Version" -o $LauncherOutput
    if ($LASTEXITCODE -ne 0) { throw 'Single-file portable launcher publish failed.' }
    $LauncherExecutable = Join-Path $LauncherOutput 'Simpler-FileBot-Portable.exe'
    if (-not (Test-Path -LiteralPath $LauncherExecutable)) { throw 'Portable launcher executable was not produced.' }
    $ExtraFiles = @(Get-ChildItem -LiteralPath $LauncherOutput -File |
        Where-Object { $_.FullName -ne $LauncherExecutable })
    if ($ExtraFiles.Count -ne 0) { throw "Portable launcher publish produced extra files: $($ExtraFiles.Name -join ', ')" }
    $PortableExecutable = Join-Path $Packages "$Name.exe"
    Copy-Item -LiteralPath $LauncherExecutable -Destination $PortableExecutable -Force
    Write-Host "Portable executable: $PortableExecutable"

    if ($Target -in @('All', 'Installer')) {
        if (-not $IsccPath) {
            $Compiler = Get-Command ISCC.exe -ErrorAction SilentlyContinue
            if ($Compiler) { $IsccPath = $Compiler.Source }
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
            throw "Portable executable built at $PortableExecutable. Install Inno Setup 6.3+ or pass -IsccPath to ISCC.exe to build the installer."
        }
        & $IsccPath "/DAppVersion=$Version" "/DAppSourceDir=$App" "/DRepoDir=$Root" `
            "/DPackageDir=$Packages" (Join-Path $Root 'packaging\windows\installer.iss')
        if ($LASTEXITCODE -ne 0) { throw 'Inno Setup failed.' }
        if (-not (Test-Path (Join-Path $Packages "Simpler-FileBot-$Version-setup-x64.exe"))) {
            throw 'The installer was not produced.'
        }
    }
    Write-Host "Build complete. Packages: $Packages"
} finally {
    Pop-Location
}
