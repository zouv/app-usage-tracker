# Shared helpers for scripts/*.ps1. ASCII only; supports Windows PowerShell 5.1 and PowerShell 7+.
$ErrorActionPreference = 'Stop'

$script:AppName = 'AppUsageTracker'
$script:Root = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$script:RuntimeDir = Join-Path $Root '.runtime'
$script:Sln = Join-Path $Root 'AppUsageTracker.sln'
$script:AppProject = Join-Path $Root 'src/AppUsageTracker/AppUsageTracker.csproj'
$script:DistDir = Join-Path $Root 'dist'
$script:RunRecord = Join-Path $RuntimeDir 'desktop.json'

function Write-Step([string]$Text) { Write-Host "[step] $Text" -ForegroundColor Cyan }
function Write-Info([string]$Text) { Write-Host "[info] $Text" }
function Write-Ok([string]$Text) { Write-Host "[ ok ] $Text" -ForegroundColor Green }
function Write-Warn([string]$Text) { Write-Host "[warn] $Text" -ForegroundColor Yellow }

function Invoke-Tool([string]$File, [string[]]$Arguments, [string]$WorkingDirectory = $Root) {
    Push-Location $WorkingDirectory
    try {
        & $File @Arguments
        if ($LASTEXITCODE -ne 0) { throw "Command failed ($LASTEXITCODE): $File $($Arguments -join ' ')" }
    } finally { Pop-Location }
}

function Require-Dotnet {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw 'dotnet CLI was not found. Install the .NET 8 SDK first.'
    }
}

function Get-AppProcesses {
    return @(Get-Process -Name $AppName -ErrorAction SilentlyContinue)
}

function Assert-AppNotRunning([string]$Action) {
    if ((Get-AppProcesses).Count -gt 0) {
        throw "Cannot $Action while $AppName is running. Exit it from the tray or use stop.bat; scripts never force-kill it."
    }
}

function Get-ManagedProcess {
    if (-not (Test-Path -LiteralPath $RunRecord -PathType Leaf)) { return $null }
    try { $saved = Get-Content -LiteralPath $RunRecord -Raw | ConvertFrom-Json } catch { return $null }
    if (-not $saved.pid) { return $null }
    $process = Get-Process -Id $saved.pid -ErrorAction SilentlyContinue
    if (-not $process -or $process.ProcessName -ne $AppName) { return $null }
    try {
        if ($process.StartTime.ToUniversalTime().Ticks.ToString() -eq [string]$saved.started) { return $process }
    } catch { }
    return $null
}

function Save-RunRecord($Process, [string]$Exe, [string]$Configuration) {
    New-Item -ItemType Directory -Path $RuntimeDir -Force | Out-Null
    $record = [ordered]@{
        pid = $Process.Id
        path = $Exe
        configuration = $Configuration
        started = $Process.StartTime.ToUniversalTime().Ticks.ToString()
        startedAt = (Get-Date).ToString('o')
    }
    [IO.File]::WriteAllText($RunRecord, ($record | ConvertTo-Json), (New-Object Text.UTF8Encoding($false)))
}

function Remove-RunRecord {
    Remove-Item -LiteralPath $RunRecord -Force -ErrorAction SilentlyContinue
}

function Get-ExePath([string]$Configuration = 'Debug') {
    $bin = Join-Path $Root "src/AppUsageTracker/bin/$Configuration"
    if (-not (Test-Path -LiteralPath $bin -PathType Container)) { return $null }
    $found = Get-ChildItem -LiteralPath $bin -Filter "$AppName.exe" -Recurse -File |
        Where-Object { $_.FullName -notmatch '[\\/]publish[\\/]' } |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($found) { return $found.FullName }
    return $null
}

# Only delete fixed paths inside the repository; reject links that could redirect deletion elsewhere.
function Assert-ManagedPath([string]$Path) {
    $base = $Root.TrimEnd('\', '/')
    $target = [IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
    if (-not $target.StartsWith($base + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Unsafe path: $target (must be inside $base)"
    }
    $probe = $target
    while ($probe.Length -gt $base.Length) {
        if ((Test-Path -LiteralPath $probe) -and
            ((Get-Item -LiteralPath $probe -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Path contains a reparse point: $probe"
        }
        $probe = Split-Path -Parent $probe
    }
    return $target
}

function Remove-ManagedPath([string]$Path) {
    $target = Assert-ManagedPath $Path
    if (-not (Test-Path -LiteralPath $target)) { return }
    for ($attempt = 1; $attempt -le 5; $attempt++) {
        try {
            Remove-Item -LiteralPath $target -Recurse -Force
            Write-Info "Removed $($target.Substring($Root.Length + 1))"
            return
        } catch {
            if ($attempt -eq 5) { throw "Cannot delete ${target}: $($_.Exception.Message)" }
            Start-Sleep -Seconds 1
        }
    }
}
