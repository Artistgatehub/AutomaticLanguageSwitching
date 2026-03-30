[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$SourceDir,

    [string]$OutputDir = (Join-Path $PSScriptRoot "payload\native-host")
)

$ErrorActionPreference = "Stop"

$resolvedSourceDir = (Resolve-Path $SourceDir).Path
$hostExePath = Join-Path $resolvedSourceDir "AutomaticLanguageSwitching.NativeHost.exe"

if (-not (Test-Path $hostExePath)) {
    throw "Native host executable not found in source directory: $hostExePath"
}

New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
Get-ChildItem -Path $OutputDir -Force | Remove-Item -Recurse -Force
Copy-Item -Path (Join-Path $resolvedSourceDir "*") -Destination $OutputDir -Recurse -Force

Write-Host "Prepared native host payload at: $OutputDir"
