[CmdletBinding()]
param(
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$AppArgs = @()
)
. (Join-Path $PSScriptRoot '_common.ps1')

# A running instance can lock build outputs: close it outright and continue.
Close-AppForDev

& (Join-Path $PSScriptRoot 'build.ps1') -Configuration Debug
& (Join-Path $PSScriptRoot 'desktop.ps1') start -Configuration Debug -AppArgs $AppArgs
