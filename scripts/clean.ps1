[CmdletBinding()]
param()
. (Join-Path $PSScriptRoot '_common.ps1')
Assert-AppNotRunning 'clean'
$targets = @()
foreach ($parent in @('src', 'tests')) {
    $directory = Join-Path $Root $parent
    if (Test-Path -LiteralPath $directory -PathType Container) {
        foreach ($project in Get-ChildItem -LiteralPath $directory -Directory) {
            $targets += (Join-Path $project.FullName 'bin'), (Join-Path $project.FullName 'obj')
        }
    }
}
$targets += $DistDir, $RuntimeDir, (Join-Path $Root 'TestResults')
foreach ($target in $targets) { Remove-ManagedPath $target }
Write-Ok 'Clean finished. User data under %AppData%/AppUsageTracker was not touched.'
