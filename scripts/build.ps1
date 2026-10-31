[CmdletBinding()]
param(
    [Parameter(Position = 0)][ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$DotnetArgs = @()
)
. (Join-Path $PSScriptRoot '_common.ps1')
Require-Dotnet
if ((Get-AppProcesses).Count -gt 0) {
    Write-Warn 'The app is running; locked output files may make the build fail. Use stop.bat or exit from the tray.'
}
Write-Step "dotnet build ($Configuration)"
Invoke-Tool 'dotnet' (@('build', $Sln, '-c', $Configuration, '--nologo') + $DotnetArgs)
$exe = Get-ExePath $Configuration
Write-Ok "Build finished$(if ($exe) { ": $exe" })."
