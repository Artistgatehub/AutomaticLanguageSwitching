[CmdletBinding()]
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA "AutomaticLanguageSwitching\NativeHost"),

    [string]$ExtensionInstallDir = (Join-Path $env:LOCALAPPDATA "AutomaticLanguageSwitching\Extension"),

    [string]$NativeHostSourceDir,

    [string]$ExtensionSourceDir,

    [switch]$SkipCopy
)

$ErrorActionPreference = "Stop"
$scriptDir = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }

$hostName = "com.automaticlanguageswitching.host"
$hostExeName = "AutomaticLanguageSwitching.NativeHost.exe"
$registryKey = "HKCU:\Software\Google\Chrome\NativeMessagingHosts\$hostName"
$repoRoot = (Resolve-Path (Join-Path $scriptDir "..\..")).Path
$templatePath = Join-Path $repoRoot "native-host\host-manifest.template.json"
$manifestPath = Join-Path $InstallDir "$hostName.json"
$installedExePath = Join-Path $InstallDir $hostExeName
$payloadRoot = Join-Path $scriptDir "payload"

if ([string]::IsNullOrWhiteSpace($NativeHostSourceDir)) {
    $NativeHostSourceDir = Join-Path $payloadRoot "native-host"
}

if ([string]::IsNullOrWhiteSpace($ExtensionSourceDir)) {
    $ExtensionSourceDir = Join-Path $payloadRoot "extension-unpacked"
}

if (-not (Test-Path $templatePath)) {
    throw "Manifest template not found: $templatePath"
}

if (-not $SkipCopy) {
    $resolvedNativeHostSourceDir = (Resolve-Path $NativeHostSourceDir).Path
    $publishedExePath = Join-Path $resolvedNativeHostSourceDir $hostExeName

    if (-not (Test-Path $publishedExePath)) {
        throw "Published native host executable not found: $publishedExePath"
    }

    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
    Copy-Item -Path (Join-Path $resolvedNativeHostSourceDir "*") -Destination $InstallDir -Recurse -Force
}
elseif (-not (Test-Path $installedExePath)) {
    throw "SkipCopy was specified, but installed executable is missing: $installedExePath"
}

if (Test-Path $ExtensionSourceDir) {
    $resolvedExtensionSourceDir = (Resolve-Path $ExtensionSourceDir).Path
    New-Item -ItemType Directory -Path $ExtensionInstallDir -Force | Out-Null
    Get-ChildItem -Path $ExtensionInstallDir -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force
    Copy-Item -Path (Join-Path $resolvedExtensionSourceDir "*") -Destination $ExtensionInstallDir -Recurse -Force
}

$manifestTemplate = Get-Content -Path $templatePath -Raw
$manifestJson = $manifestTemplate.Replace("__HOST_EXE_PATH__", $installedExePath.Replace("\", "\\"))

Set-Content -Path $manifestPath -Value $manifestJson -Encoding utf8

reg.exe add "HKCU\Software\Google\Chrome\NativeMessagingHosts\$hostName" /ve /t REG_SZ /d $manifestPath /f | Out-Null

Write-Host "Installed native host to: $InstallDir"
Write-Host "Manifest written to: $manifestPath"
Write-Host "Chrome registry key updated: $registryKey"
if (Test-Path $ExtensionInstallDir) {
    Write-Host "Prepared unpacked extension at: $ExtensionInstallDir"
}
