[CmdletBinding()]
param(
    [string]$ExtensionDir = (Join-Path (Join-Path $PSScriptRoot "..\..") "extension"),

    [string]$OutputDir = (Join-Path $PSScriptRoot "payload\extension-unpacked")
)

$ErrorActionPreference = "Stop"

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
