[CmdletBinding()]
param(
    [Parameter(Position = 0)][ValidateSet('self', 'fx')][string]$Mode = 'self',
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$DotnetArgs = @()
)
. (Join-Path $PSScriptRoot '_common.ps1')
Require-Dotnet
Assert-AppNotRunning 'publish'
$env:DOTNET_CLI_USE_MSBUILD_SERVER = '0'
$env:DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER = '1'
$selfContained = if ($Mode -eq 'self') { 'true' } else { 'false' }
$stage = Assert-ManagedPath (Join-Path $RuntimeDir 'package')
$appDir = Join-Path $stage 'app'
Remove-ManagedPath $stage
New-Item -ItemType Directory -Path $appDir -Force | Out-Null

try {
    Write-Step "Clean project outputs for reliable single-file publish ($Mode)"
    foreach ($project in @('src/AppUsageTracker', 'tests/AppUsageTracker.Tests')) {
        Remove-ManagedPath (Join-Path $Root "$project/bin")
        Remove-ManagedPath (Join-Path $Root "$project/obj")
    }

    Write-Step 'dotnet publish to staging'
    Invoke-Tool 'dotnet' (@(
        'publish', $AppProject, '-c', 'Release', '-r', 'win-x64',
        '--self-contained', $selfContained,
        '-p:PublishSingleFile=true', '-p:PublishReadyToRun=false',
        '-o', $appDir, '--nologo'
    ) + $DotnetArgs)

    $files = @(Get-ChildItem -LiteralPath $appDir -File -Force)
    $exe = Join-Path $appDir "$AppName.exe"
    if ($files.Count -ne 1 -or -not (Test-Path -LiteralPath $exe -PathType Leaf)) {
        $names = ($files | ForEach-Object Name) -join ', '
        throw "Single-file verification failed. Staged files: $names"
    }
    Write-Ok "Verified staged AppUsageTracker.exe ($((Get-Item -LiteralPath $exe).Length) bytes)."

    # Keep the previous dist/ until publish and verification have both succeeded.
    # Roll it back if moving the verified app into place fails.
    $previousDist = Join-Path $stage 'previous-dist'
    $hadPreviousDist = Test-Path -LiteralPath $DistDir -PathType Container
    if ($hadPreviousDist) {
        Assert-ManagedPath $DistDir | Out-Null
        Move-Item -LiteralPath $DistDir -Destination $previousDist
    }
    try {
        Move-Item -LiteralPath $appDir -Destination $DistDir
    } catch {
        if ($hadPreviousDist -and -not (Test-Path -LiteralPath $DistDir)) {
            Move-Item -LiteralPath $previousDist -Destination $DistDir
        }
        throw
    }
    Remove-ManagedPath $stage
    Write-Ok "Published: $(Join-Path $DistDir "$AppName.exe")"
} catch {
    Write-Warn "Publish failed; staging was kept for inspection: $stage"
    throw
}
