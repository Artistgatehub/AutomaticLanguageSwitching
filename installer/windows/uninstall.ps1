[CmdletBinding()]
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA "AutomaticLanguageSwitching\NativeHost"),

    [switch]$KeepFiles
)

$ErrorActionPreference = "Stop"

$hostName = "com.automaticlanguageswitching.host"
$registryKey = "HKCU:\Software\Google\Chrome\NativeMessagingHosts\$hostName"

if (Test-Path $registryKey) {
    Remove-Item -Path $registryKey -Recurse -Force
    Write-Host "Removed Chrome native host registration: $registryKey"
}
else {
    Write-Host "Chrome native host registration not found: $registryKey"
}

if (-not $KeepFiles) {
    if (Test-Path $InstallDir) {
        Remove-Item -Path $InstallDir -Recurse -Force
        Write-Host "Removed installed files from: $InstallDir"
    }
    else {
        Write-Host "Install directory not found: $InstallDir"
    }
}
else {
    Write-Host "Installed files were kept at: $InstallDir"
}
