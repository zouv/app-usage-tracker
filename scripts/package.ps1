[CmdletBinding()]
param(
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$PublishArgs = @()
)
. (Join-Path $PSScriptRoot '_common.ps1')
. (Join-Path $PSScriptRoot 'PackageTransaction.ps1')
Require-Dotnet

# A persistent MSBuild/Roslyn server can cache a non-single-file evaluation and
# silently skip GenerateBundle during publish. Disable it for this process.
$env:DOTNET_CLI_USE_MSBUILD_SERVER = '0'
$env:DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER = '1'

$stagingRoot = Assert-ManagedPath (Join-Path $RuntimeDir 'staging')
$stagingDir = Assert-ManagedPath (Join-Path $stagingRoot ('package-' + [Guid]::NewGuid().ToString('N')))
$finalDevDir = Join-Path $DistDir 'dev'
$finalReleaseDir = Join-Path $DistDir 'release'
$devDir = Join-Path $stagingDir 'dev'
$releaseDir = Join-Path $stagingDir 'release'

# Version and target framework are single-sourced from the app project.
$version = (& dotnet msbuild $AppProject -getProperty:Version -nologo 2>$null | Select-Object -Last 1)
if ($null -ne $version) { $version = $version.Trim() }
if ([string]::IsNullOrEmpty($version) -or $version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Cannot read <Version> from $AppProject via dotnet msbuild."
}
$tfm = (& dotnet msbuild $AppProject -getProperty:TargetFramework -nologo 2>$null | Select-Object -Last 1)
if ($null -ne $tfm) { $tfm = $tfm.Trim() }
if ([string]::IsNullOrEmpty($tfm)) {
    throw "Cannot read <TargetFramework> from $AppProject via dotnet msbuild."
}

$devSourceRoot = Join-Path $stagingDir 'debug-source'
$devUnpack = Join-Path $devSourceRoot "${AppName}_${version}_x64-unpack-debug"
$devLogs = Join-Path $devUnpack 'logs'
$devZip = Join-Path $devDir "${AppName}_${version}_x64-unpack-debug.zip"
$relUnpack = Join-Path $releaseDir 'unpack'
$relZip = Join-Path $releaseDir "${AppName}_${version}_x64-unpack.zip"

# A running instance can lock dist files. Never close it silently: list the
# processes, ask first, and only continue after an explicit Y.
$running = @(Get-AppProcesses)
if ($running.Count -gt 0) {
    Write-Host ''
    Write-Warn "Detected a running $AppName instance:"
    foreach ($process in $running) {
        $processPath = $null
        try { $processPath = $process.Path } catch { }
        if ([string]::IsNullOrEmpty($processPath)) { $processPath = '(path unavailable)' }
        Write-Host ("       PID {0}  {1}" -f $process.Id, $processPath)
    }
    Write-Warn 'Closing it may end the current tracking session; sessions are saved on a normal exit.'
    if ([Console]::IsInputRedirected) {
        throw "$AppName is still running. Exit it before packaging (non-interactive input cannot confirm closing it)."
    }
    $answer = Read-Host 'Close it and continue packaging? Enter Y to confirm; anything else cancels'
    if ($answer -notmatch '^\s*[Yy]([Ee][Ss])?\s*$') {
        Write-Warn 'Packaging cancelled. Exit the app manually and try again.'
        exit 1
    }
    foreach ($process in $running) {
        try { [void]$process.CloseMainWindow() } catch { }
    }
    # Give the normal close (tray exit, session save) a bounded chance first.
    $deadline = (Get-Date).AddSeconds(12)
    while ((Get-AppProcesses).Count -gt 0 -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 250
    }
    foreach ($process in @(Get-AppProcesses)) {
        try { $process.Kill(); [void]$process.WaitForExit(5000) } catch { }
    }
    if ((Get-AppProcesses).Count -gt 0) {
        throw "Could not close the running $AppName instance (administrator rights may be required). Exit it manually and retry."
    }
    Write-Ok "Closed the running $AppName instance; continuing."
}

$license = $null
$licensePath = Join-Path $Root 'LICENSE'
if (Test-Path -LiteralPath $licensePath -PathType Leaf) { $license = $licensePath }

# Native stderr redirection under Windows PowerShell 5.1 can become a
# terminating error when $ErrorActionPreference is Stop; keep it non-terminating
# for the duration of the call and evaluate the exit code explicitly.
function Invoke-NativeLogged([string]$File, [string[]]$Arguments, [string]$Log) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & $File @Arguments 2>&1 | Out-File -LiteralPath $Log -Encoding utf8
        return $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $previous
    }
}

function Invoke-Build([string]$Configuration, [string]$Log) {
    $exitCode = Invoke-NativeLogged 'dotnet' @('build', $Sln, '-c', $Configuration, '--nologo', '-v:minimal') $Log
    if ($exitCode -ne 0) {
        Get-Content -LiteralPath $Log -Tail 40
        throw "$Configuration build failed. Log: $Log"
    }
}

function Copy-DevFiles([string]$Source, [string]$Destination) {
    if (-not (Test-Path -LiteralPath $Source -PathType Container)) {
        throw "Debug output not found: $Source"
    }
    Get-ChildItem -LiteralPath $Source -Force | Copy-Item -Destination $Destination -Recurse -Force
    foreach ($name in @("$AppName.exe", "$AppName.dll", "$AppName.deps.json", "$AppName.runtimeconfig.json")) {
        $file = Join-Path $Destination $name
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Missing debug output: $file" }
    }
    $dll = Join-Path $Destination "$AppName.dll"
    $builtVersion = [Reflection.AssemblyName]::GetAssemblyName($dll).Version.ToString(3)
    if ($builtVersion -ne $version) { throw "Stale debug build: $dll ($builtVersion != $version)" }
}

function Invoke-ReleasePublish([string]$Destination, [string]$Log) {
    $arguments = @(
        'publish', $AppProject, '-c', 'Release', '-r', 'win-x64',
        '--self-contained', 'true',
        '-p:PublishSingleFile=true',
        '-p:PublishReadyToRun=false',
        '-p:DebugType=None',
        '-p:DebugSymbols=false',
        '-o', $Destination,
        '--nologo'
    ) + $PublishArgs
    $exitCode = Invoke-NativeLogged 'dotnet' $arguments $Log
    if ($exitCode -ne 0) {
        Get-Content -LiteralPath $Log -Tail 40
        throw "Release publish failed. Log: $Log"
    }
}

function Test-ReleaseOutput([string]$Directory) {
    $files = @(Get-ChildItem -LiteralPath $Directory -File -Force)
    $exe = Join-Path $Directory "$AppName.exe"
    if ($files.Count -ne 1 -or -not (Test-Path -LiteralPath $exe -PathType Leaf)) {
        $names = ($files | ForEach-Object Name) -join ', '
        throw "Single-file verification failed. Staged files: $names"
    }
    $fileVersion = (Get-Item -LiteralPath $exe).VersionInfo.FileVersion
    $parsed = $null
    try { $parsed = [Version]$fileVersion } catch { $parsed = $null }
    if ($null -eq $parsed -or $parsed.ToString(3) -ne $version) {
        throw "Stale release build: $exe (FileVersion '$fileVersion' != $version)"
    }
    Write-Ok "Verified $AppName.exe ($((Get-Item -LiteralPath $exe).Length) bytes, version $version)."
}

function Write-BuildInfo([string]$Path) {
    $git = 'unknown'
    try {
        $commit = (& git -C $Root rev-parse --short HEAD 2>$null | Select-Object -First 1)
        if (-not [string]::IsNullOrEmpty($commit)) { $git = $commit.Trim() }
    } catch { }
    $sdk = (& dotnet --version 2>$null | Select-Object -First 1)
    @(
        "$AppName $version (dev)"
        "Built: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
        "Git: $git"
        "Dotnet: $sdk"
        "OS: $([Environment]::OSVersion.VersionString)"
    ) | Set-Content -LiteralPath $Path -Encoding UTF8
}

function Copy-ReleaseDocs([string]$Destination) {
    foreach ($name in @('README.md', 'README-EN.md')) {
        $source = Join-Path $Root $name
        if (Test-Path -LiteralPath $source -PathType Leaf) {
            Copy-Item -LiteralPath $source -Destination (Join-Path $Destination $name) -Force
        }
    }
    # README images use relative paths; include them so the packaged README
    # also renders offline.
    foreach ($relative in @(
        'docs/assets/software-usage-duration-design/01-overview.png',
        'docs/assets/software-usage-duration-design/02-software-management.png',
        'docs/assets/software-usage-duration-design/03-statistics.png',
        'docs/assets/software-usage-duration-design/04-settings.png'
    )) {
        $source = Join-Path $Root $relative
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Missing release asset: $source" }
        $target = Join-Path $Destination $relative
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
        Copy-Item -LiteralPath $source -Destination $target -Force
    }
}

# Create a zip whose entries are relative to $Source; $Prefix adds a top-level
# folder (dev keeps one, release does not, matching the release convention).
function New-Zip([string]$Source, [string]$ZipPath, [string]$Prefix) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    if (Test-Path -LiteralPath $ZipPath) { Remove-Item -LiteralPath $ZipPath -Force }
    $archive = [System.IO.Compression.ZipFile]::Open($ZipPath, 'Create')
    try {
        $base = [IO.Path]::GetFullPath($Source).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
        foreach ($file in Get-ChildItem -LiteralPath $Source -Recurse -File) {
            $relative = $file.FullName.Substring($base.Length).Replace('\', '/')
            $entry = if ([string]::IsNullOrEmpty($Prefix)) { $relative } else { "$Prefix/$relative" }
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $entry) | Out-Null
        }
    } finally {
        $archive.Dispose()
    }
}

Write-Host "Package | Version = $version"
Write-Host "  dev zip : $devZip"
Write-Host "  unpack  : $relUnpack"
Write-Host "  zip     : $relZip"

# Serialize builds/publication. Keep the common lock file (unlinking would race waiters).
New-Item -ItemType Directory -Force -Path $stagingRoot | Out-Null
$packageLock = [IO.File]::Open((Join-Path $stagingRoot 'package.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
try {
    New-Item -ItemType Directory -Force -Path $devLogs, $relUnpack, $devDir, $releaseDir | Out-Null

    # 1) dev: Debug build with build log and build metadata for troubleshooting.
    Write-Step '[1/4] Debug build...'
    Invoke-Build 'Debug' (Join-Path $devLogs 'dev-build.log')
    $devSource = Join-Path $Root "src/AppUsageTracker/bin/Debug/$tfm"
    Copy-DevFiles $devSource $devUnpack
    if ($license) { Copy-Item -LiteralPath $license -Destination (Join-Path $devUnpack 'LICENSE') -Force }

    @(
        "$AppName $version (Debug, x64)"
        "======================================="
        ""
        "Debug build for development and troubleshooting."
        "Requires Windows 10/11 x64 and the .NET 8 Desktop Runtime (included with the .NET 8 SDK)."
        ""
        "Debug info: logs\, build-info.txt. Debug symbols are embedded in the assemblies."
        "User data: %AppData%\AppUsageTracker"
    ) | Set-Content -LiteralPath (Join-Path $devUnpack 'README.txt') -Encoding UTF8
    Write-BuildInfo (Join-Path $devUnpack 'build-info.txt')

    # 2) release: self-contained single-file publish, runnable in place.
    Write-Step '[2/4] Release publish...'
    Invoke-ReleasePublish $relUnpack (Join-Path $devLogs 'release-publish.log')
    Test-ReleaseOutput $relUnpack
    Copy-ReleaseDocs $relUnpack
    if ($license) { Copy-Item -LiteralPath $license -Destination (Join-Path $relUnpack 'LICENSE') -Force }

    @(
        "$AppName $version (x64)"
        "========================="
        ""
        "Portable release: double-click $AppName.exe. No installer or admin rights needed."
        "Requires Windows 10/11 x64. The .NET 8 runtime is bundled in the executable."
        ""
        "See README.md for usage. User data: %AppData%\AppUsageTracker"
    ) | Set-Content -LiteralPath (Join-Path $relUnpack 'README.txt') -Encoding UTF8

    # 3) Zip: dev keeps its folder root; release entries sit directly at zip root.
    Write-Step '[3/4] Creating dev zip...'
    New-Zip $devUnpack $devZip "${AppName}_${version}_x64-unpack-debug"

    # 4) Release zip and cleanup. Staging is removed only after both zips succeed;
    #    dist\release\unpack is the official runnable output and stays.
    Write-Step '[4/4] Creating release zip...'
    New-Zip $relUnpack $relZip ''
    Test-PackageZip $devUnpack $devZip "${AppName}_${version}_x64-unpack-debug"
    Test-PackageZip $relUnpack $relZip ''
    New-Item -ItemType Directory -Force -Path $DistDir | Out-Null
    Publish-PackageSet $Root $stagingDir $DistDir
    $cleanupPath = Assert-PackagePath $stagingDir $Root
    try {
        Remove-Item -LiteralPath $cleanupPath -Recurse -Force
    } catch {
        Write-Warn "Packages were published, but staging cleanup failed: $cleanupPath"
    }

    # Remove the legacy single-file output from the pre-dev/release layout.
    $legacyExe = Join-Path $DistDir "$AppName.exe"
    if (Test-Path -LiteralPath $legacyExe) {
        try { Remove-ManagedPath $legacyExe } catch { Write-Warn "Could not remove legacy output: $legacyExe" }
    }

    Write-Host ''
    Write-Host 'Output files:'
    foreach ($file in Get-ChildItem -LiteralPath $finalDevDir, $finalReleaseDir -File | Sort-Object FullName) {
        Write-Host ("  {0}  {1} bytes" -f $file.FullName.Substring($DistDir.Length + 1), $file.Length)
    }
    Write-Host ''
    Write-Ok "Package complete (version $version)."
    Write-Host "  dev    : $(Join-Path $finalDevDir (Split-Path -Leaf $devZip))"
    Write-Host "  unpack : $(Join-Path $finalReleaseDir 'unpack')"
    Write-Host "  zip    : $(Join-Path $finalReleaseDir (Split-Path -Leaf $relZip))"
} finally {
    $packageLock.Dispose()
}
