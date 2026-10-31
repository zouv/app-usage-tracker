[CmdletBinding()]
param(
    [Parameter(Position = 0)][ValidateSet('start', 'stop', 'restart', 'status')][string]$Command = 'status',
    [Parameter(Position = 1)][ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [int]$StopTimeoutSeconds = 30,
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$AppArgs = @()
)
. (Join-Path $PSScriptRoot '_common.ps1')

function Show-Status {
    $managed = Get-ManagedProcess
    $processes = Get-AppProcesses
    if ($processes.Count -eq 0) {
        Write-Info 'Not running.'
        Remove-RunRecord
    } else {
        foreach ($process in $processes) {
            $origin = if ($managed -and $managed.Id -eq $process.Id) { 'script-managed' } else { 'external instance' }
            Write-Ok "Running: pid $($process.Id), $origin, started $($process.StartTime.ToString('o'))"
        }
    }
    Write-Info "Run record: $RunRecord"
    Write-Info 'Logs: %AppData%/AppUsageTracker/logs'
}

function Start-App {
    if ((Get-AppProcesses).Count -gt 0) {
        Write-Info 'An instance is already running; no second instance was started.'
        Show-Status
        return
    }
    $exe = Get-ExePath $Configuration
    if (-not $exe) {
        Write-Info "No $Configuration build found; building first."
        & (Join-Path $PSScriptRoot 'build.ps1') -Configuration $Configuration
        if ($LASTEXITCODE -ne 0) { throw 'Build failed; the app was not started.' }
        $exe = Get-ExePath $Configuration
    }
    if (-not $exe) { throw "No $Configuration executable was found after the build." }

    $startArgs = @{ FilePath = $exe; WorkingDirectory = (Split-Path -Parent $exe); PassThru = $true }
    if ($AppArgs.Count -gt 0) { $startArgs.ArgumentList = $AppArgs }
    $process = Start-Process @startArgs
    Start-Sleep -Milliseconds 1500
    if ($process.HasExited) {
        if ($process.ExitCode -eq 0) {
            Write-Info 'The process exited normally. Another instance may have been brought to the foreground.'
            return
        }
        throw "The app exited immediately with code $($process.ExitCode). Check %AppData%/AppUsageTracker/logs."
    }
    Save-RunRecord $process $exe $Configuration
    Write-Ok "Started $AppName (pid $($process.Id), $Configuration)."
}

function Stop-App {
    $targets = Get-AppProcesses
    if ($targets.Count -eq 0) {
        Write-Info 'Not running.'
        Remove-RunRecord
        return
    }
    foreach ($process in $targets) {
        Write-Info "Requesting normal close for pid $($process.Id) (never force-killed)."
        [void]$process.CloseMainWindow()
    }
    $deadline = (Get-Date).AddSeconds([Math]::Max(1, $StopTimeoutSeconds))
    do {
        $alive = @($targets | Where-Object {
            try { $_.Refresh(); -not $_.HasExited } catch { $false }
        })
        if ($alive.Count -eq 0) {
            Remove-RunRecord
            Write-Ok 'Stopped normally.'
            return
        }
        Start-Sleep -Milliseconds 300
    } while ((Get-Date) -lt $deadline)
    throw "The app is still running after $StopTimeoutSeconds seconds. If it is hidden in the tray, choose Exit from the tray menu; this script never force-kills it."
}

switch ($Command) {
    'start' { Start-App }
    'stop' { Stop-App }
    'restart' { Stop-App; Start-App }
    'status' { Show-Status }
}
