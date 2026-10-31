[CmdletBinding()]
param(
    [Parameter(Position = 0)][ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$DotnetArgs = @()
)
. (Join-Path $PSScriptRoot '_common.ps1')
Require-Dotnet
$failed = @()
$projects = @(Get-ChildItem -Path (Join-Path $Root 'tests') -Filter '*.csproj' -Recurse -File | Sort-Object FullName)
if ($projects.Count -eq 0) { throw 'No test projects were found under tests/.' }
foreach ($project in $projects) {
    Write-Step "dotnet test $($project.BaseName) ($Configuration)"
    & dotnet test $project.FullName -c $Configuration --nologo --logger 'console;verbosity=normal' @DotnetArgs
    if ($LASTEXITCODE -ne 0) { $failed += $project.BaseName }
}
if ($failed.Count -gt 0) { throw "Tests failed: $($failed -join ', ')" }
Write-Ok "All $($projects.Count) test project(s) passed."
