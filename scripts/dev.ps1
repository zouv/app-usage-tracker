[CmdletBinding()]
param(
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$AppArgs = @()
)
. (Join-Path $PSScriptRoot '_common.ps1')

# A running instance can lock build outputs. Never force-kill it just to rebuild.
if ((Get-AppProcesses).Count -gt 0) {
    Write-Info 'The app is running; requesting a normal close before rebuilding.'
    & (Join-Path $PSScriptRoot 'desktop.ps1') stop
}

& (Join-Path $PSScriptRoot 'build.ps1') -Configuration Debug
& (Join-Path $PSScriptRoot 'desktop.ps1') start -Configuration Debug -AppArgs $AppArgs
