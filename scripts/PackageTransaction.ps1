# Package staging helpers. ASCII only; supports Windows PowerShell 5.1 and PowerShell 7+.
# Every recursive move/delete stays inside the workspace passed by the caller.
function Assert-PackagePath([string]$Path, [string]$Workspace) {
    $absolute = [IO.Path]::GetFullPath($Path)
    $base = [IO.Path]::GetFullPath($Workspace).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if (-not $absolute.StartsWith($base, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Package target escapes workspace: $absolute"
    }
    return $absolute
}

# Replace dist\dev and dist\release as one transaction. Existing directories are
# moved aside first and restored in reverse order when any move fails.
function Publish-PackageSet([string]$Workspace, [string]$Stage, [string]$Dist) {
    $backup = Assert-PackagePath (Join-Path $Stage 'previous') $Workspace
    New-Item -ItemType Directory -Path $backup -Force | Out-Null
    $saved = @()
    $promoted = @()
    try {
        foreach ($name in @('dev', 'release')) {
            $source = Assert-PackagePath (Join-Path $Stage $name) $Workspace
            $target = Assert-PackagePath (Join-Path $Dist $name) $Workspace
            $old = Assert-PackagePath (Join-Path $backup $name) $Workspace
            if (Test-Path -LiteralPath $target) {
                Move-Item -LiteralPath $target -Destination $old
                $saved += $name
            }
            Move-Item -LiteralPath $source -Destination $target
            $promoted += $name
        }
    } catch {
        $failure = $_
        $rollback = @()
        foreach ($name in @('release', 'dev')) {
            $source = Assert-PackagePath (Join-Path $Stage $name) $Workspace
            $target = Assert-PackagePath (Join-Path $Dist $name) $Workspace
            $old = Assert-PackagePath (Join-Path $backup $name) $Workspace
            try {
                if ($promoted -contains $name) { Move-Item -LiteralPath $target -Destination $source }
                if ($saved -contains $name) { Move-Item -LiteralPath $old -Destination $target }
            } catch {
                $rollback += $_.Exception.Message
            }
        }
        if ($rollback.Count -gt 0) {
            throw "Publish failed: $failure; rollback incomplete, retained at $Stage : $($rollback -join '; ')"
        }
        throw $failure
    }
}

# Compare every zip entry with the staged file: name, size and SHA-256 content.
function Test-PackageZip([string]$Source, [string]$ZipPath, [string]$Prefix) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        $base = [IO.Path]::GetFullPath($Source).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
        $files = @(Get-ChildItem -LiteralPath $Source -Recurse -File)
        if ($zip.Entries.Count -ne $files.Count) { throw "Zip entry count mismatch: $ZipPath" }
        foreach ($file in $files) {
            $relative = $file.FullName.Substring($base.Length).Replace('\', '/')
            $name = if ([string]::IsNullOrEmpty($Prefix)) { $relative } else { "$Prefix/$relative" }
            $entry = $zip.GetEntry($name)
            if ($null -eq $entry -or $entry.Length -ne $file.Length) { throw "Zip entry missing or invalid: $name" }
            $input = $entry.Open()
            $disk = [IO.File]::OpenRead($file.FullName)
            $sha = [Security.Cryptography.SHA256]::Create()
            try {
                $actual = [Convert]::ToBase64String($sha.ComputeHash($input))
                $expected = [Convert]::ToBase64String($sha.ComputeHash($disk))
                if ($actual -ne $expected) { throw "Zip content mismatch: $name" }
            } finally {
                $sha.Dispose()
                $input.Dispose()
                $disk.Dispose()
            }
        }
    } finally {
        $zip.Dispose()
    }
}
