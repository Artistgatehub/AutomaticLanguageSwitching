[CmdletBinding()]
param(
    [string]$ExtensionDir,

    [string]$OutputDir
)

$ErrorActionPreference = "Stop"
$scriptDir = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
$repoRoot = (Resolve-Path (Join-Path $scriptDir "..\..")).Path

if ([string]::IsNullOrWhiteSpace($ExtensionDir)) {
    $ExtensionDir = Join-Path $repoRoot "extension"
}

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $scriptDir "payload\extension-unpacked"
}

$resolvedExtensionDir = (Resolve-Path $ExtensionDir).Path
$manifestPath = Join-Path $resolvedExtensionDir "manifest.json"
$distDir = Join-Path $resolvedExtensionDir "dist"

if (-not (Test-Path $manifestPath)) {
    throw "Extension manifest not found: $manifestPath"
}

if (-not (Test-Path $distDir)) {
    throw "Extension dist folder not found: $distDir. Build the extension before preparing the payload."
}

New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
Get-ChildItem -Path $OutputDir -Force | Remove-Item -Recurse -Force

Copy-Item -Path $manifestPath -Destination $OutputDir -Force
Copy-Item -Path $distDir -Destination $OutputDir -Recurse -Force

Write-Host "Prepared unpacked extension payload at: $OutputDir"
