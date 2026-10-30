[CmdletBinding()]
param([Parameter(Mandatory)][string]$Directory)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'Run this smoke test on Windows.' }

$Executable = Join-Path $Directory 'Simpler-FileBot.exe'
if (-not (Test-Path -LiteralPath $Executable)) { throw "Executable not found: $Executable" }
$Started = Get-Date
$Process = $null
try {
    $Process = Start-Process -FilePath $Executable -WorkingDirectory $Directory -PassThru
    if (-not $Process.WaitForExit(15000)) {
        Write-Host 'Portable app stayed open for 15 seconds.'
        return
    }

    $ExitCode = $Process.ExitCode
    Write-Host "Portable app exited during startup with code $ExitCode."
    Start-Sleep -Seconds 3
    $StartupLog = Join-Path $env:LOCALAPPDATA 'Simpler FileBot\startup-error.log'
    if ((Test-Path -LiteralPath $StartupLog) -and (Get-Item -LiteralPath $StartupLog).LastWriteTime -ge $Started) {
        Write-Host "Startup log: $(Get-Content -LiteralPath $StartupLog -Raw)"
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
    if ($Process) {
        if (-not $Process.HasExited) { $Process.Kill(); $Process.WaitForExit(5000) | Out-Null }
        $Process.Dispose()
    }
}
