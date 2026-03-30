[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$SourceDir,

    [string]$OutputDir
)

$ErrorActionPreference = "Stop"
$scriptDir = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $scriptDir "payload\native-host"
}

$resolvedSourceDir = (Resolve-Path $SourceDir).Path
$hostExePath = Join-Path $resolvedSourceDir "AutomaticLanguageSwitching.NativeHost.exe"

if (-not (Test-Path $hostExePath)) {
    throw "Native host executable not found in source directory: $hostExePath"
}

New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
Get-ChildItem -Path $OutputDir -Force | Remove-Item -Recurse -Force
Copy-Item -Path (Join-Path $resolvedSourceDir "*") -Destination $OutputDir -Recurse -Force

Write-Host "Prepared native host payload at: $OutputDir"
