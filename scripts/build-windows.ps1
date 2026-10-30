[CmdletBinding()]
param(
    [ValidateSet('All', 'Portable', 'Installer')]
    [string]$Target = 'All',
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '2.1.1',
    [string]$DotNet = 'dotnet',
    [string]$IsccPath = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'Run this script on Windows x64.' }
$Root = Split-Path $PSScriptRoot -Parent
$Project = Join-Path $Root 'src\SimplerFileBot.WinUI\SimplerFileBot.WinUI.csproj'
$Tests = Join-Path $Root 'tests\SimplerFileBot.Core.Tests\SimplerFileBot.Core.Tests.csproj'
$Dist = Join-Path $Root 'dist\windows'
$Build = Join-Path $Root 'build\windows'
$Packages = Join-Path $Dist 'packages'
$App = Join-Path $Dist 'Simpler-FileBot'

& $DotNet --version
if ($LASTEXITCODE -ne 0) { throw 'Install .NET SDK 10 x64 and make dotnet available on PATH.' }

if ($Target -ne 'Portable') {
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
        throw 'Install Inno Setup 6.3+ or pass -IsccPath to ISCC.exe. Portable builds do not need Inno Setup.'
    }
}

Push-Location $Root
try {
    & $DotNet test $Tests -c Release
    if ($LASTEXITCODE -ne 0) { throw '.NET tests failed.' }
    if (Test-Path -LiteralPath $App) { Remove-Item -LiteralPath $App -Recurse -Force }
    & $DotNet publish $Project -c Release -r win-x64 --self-contained true `
        -p:WindowsAppSDKSelfContained=true -o $App
    if ($LASTEXITCODE -ne 0) { throw 'WinUI 3 publish failed.' }
    $Executable = Join-Path $App 'Simpler-FileBot.exe'
    if (-not (Test-Path -LiteralPath $Executable)) { throw 'Application executable was not produced.' }
    New-Item -ItemType Directory -Force -Path $Packages | Out-Null

    if ($Target -in @('All', 'Portable')) {
        $Name = "Simpler-FileBot-$Version-portable-x64"
        $Stage = Join-Path (Join-Path $Build 'portable') $Name
        if (Test-Path -LiteralPath $Stage) { Remove-Item -LiteralPath $Stage -Recurse -Force }
        New-Item -ItemType Directory -Force -Path $Stage | Out-Null
        Copy-Item -Path (Join-Path $App '*') -Destination $Stage -Recurse
        Copy-Item -LiteralPath (Join-Path $Root 'LICENSE') -Destination $Stage
        Set-Content -LiteralPath (Join-Path $Stage 'portable.flag') -Value '' -Encoding ASCII
        Set-Content -LiteralPath (Join-Path $Stage 'README-portable.txt') -Encoding UTF8 -Value @'
Run Simpler-FileBot.exe. Keep the entire extracted folder together.
No Python or .NET installation is required on the destination computer.
Settings and API keys are saved in the data folder beside the executable.
Use a writable folder, and remove data before sharing a used copy.
'@
        Compress-Archive -LiteralPath $Stage -DestinationPath (Join-Path $Packages "$Name.zip") -Force
    }
    if ($Target -in @('All', 'Installer')) {
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
