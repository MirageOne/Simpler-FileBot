[CmdletBinding()]
param(
    [string]$Directory,
    [string]$Executable,
    [ValidateRange(1, 120)][int]$WaitSeconds = 45
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'Run this smoke test on Windows.' }

if (-not $Executable -and -not $Directory) { throw 'Pass -Executable for the portable EXE or -Directory for an unpacked app.' }
if (-not $Executable) { $Executable = Join-Path $Directory 'Simpler-FileBot.exe' }
if (-not (Test-Path -LiteralPath $Executable)) { throw "Executable not found: $Executable" }
$Executable = (Resolve-Path -LiteralPath $Executable).Path
$WorkingDirectory = Split-Path $Executable -Parent
$IsLauncher = (Split-Path $Executable -Leaf) -ne 'Simpler-FileBot.exe'
$Started = Get-Date
$Process = $null
$Children = @()
try {
    $Process = Start-Process -FilePath $Executable -WorkingDirectory $WorkingDirectory -PassThru
    if (-not $Process.WaitForExit($WaitSeconds * 1000)) {
        $Process.Refresh()
        $WindowHandle = $Process.MainWindowHandle
        if ($IsLauncher) {
            $ChildIds = @(Get-CimInstance Win32_Process -Filter "ParentProcessId = $($Process.Id)" |
                Where-Object { $_.Name -eq 'Simpler-FileBot.exe' } |
                Select-Object -ExpandProperty ProcessId)
            foreach ($ChildId in $ChildIds) {
                $Child = Get-Process -Id $ChildId -ErrorAction SilentlyContinue
                if ($Child) { $Child.Refresh(); $Children += $Child; $WindowHandle = $Child.MainWindowHandle }
            }
            $SettingsFile = Join-Path $WorkingDirectory 'data\settings.json'
            if (-not (Test-Path -LiteralPath $SettingsFile)) {
                throw "Portable settings were not created beside the single executable: $SettingsFile"
            }
        }
        if ($WindowHandle -eq [IntPtr]::Zero) {
            throw 'Portable app stayed running but did not create a main window.'
        }
        Write-Host "Portable app displayed a window, stored settings beside the executable, and stayed open for $WaitSeconds seconds."
        return
    }

    $ExitCode = $Process.ExitCode
    Write-Host "Portable app exited during startup with code $ExitCode."
    Start-Sleep -Seconds 3
    $StartupLog = Join-Path $env:LOCALAPPDATA 'Simpler FileBot\startup-error.log'
    if ((Test-Path -LiteralPath $StartupLog) -and (Get-Item -LiteralPath $StartupLog).LastWriteTime -ge $Started) {
        Write-Host "Startup log: $(Get-Content -LiteralPath $StartupLog -Raw)"
    }
    $LauncherLog = Join-Path $WorkingDirectory 'data\launcher-error.log'
    if ((Test-Path -LiteralPath $LauncherLog) -and (Get-Item -LiteralPath $LauncherLog).LastWriteTime -ge $Started) {
        Write-Host "Launcher log: $(Get-Content -LiteralPath $LauncherLog -Raw)"
    }
    $Events = Get-WinEvent -FilterHashtable @{ LogName = 'Application'; StartTime = $Started } -ErrorAction SilentlyContinue |
        Where-Object { $_.ProviderName -in @('.NET Runtime', 'Application Error', 'Windows Error Reporting') -or
                       $_.Message -like '*Simpler-FileBot*' } |
        Select-Object -First 5
    foreach ($Event in $Events) {
        Write-Host "Event $($Event.ProviderName) $($Event.Id): $($Event.Message)"
    }
    throw "Portable app exited during startup with code $ExitCode."
} finally {
    foreach ($Child in $Children) {
        if (-not $Child.HasExited) { $Child.Kill(); $Child.WaitForExit(5000) | Out-Null }
        $Child.Dispose()
    }
    if ($Process) {
        if (-not $Process.HasExited -and -not $Process.WaitForExit(10000)) {
            $Process.Kill(); $Process.WaitForExit(5000) | Out-Null
        }
        $Process.Dispose()
    }
}
