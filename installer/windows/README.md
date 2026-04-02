# Windows Installer Preparation

This folder contains packaging and release files for the Windows installer.

## Payload Layout Convention

Future installer payloads are staged under:

- `installer/windows/payload/native-host`
- `installer/windows/payload/extension-unpacked`

The intention is:

- the native host payload is copied into a Windows install directory
- the unpacked extension payload is copied into a predictable local folder for Chrome's `Load unpacked` flow

Recommended installed paths:

- native host: `%LOCALAPPDATA%\AutomaticLanguageSwitching\NativeHost`
- unpacked extension: `%LOCALAPPDATA%\AutomaticLanguageSwitching\Extension`

## What `install.ps1` Does

- installs the native host from a staged payload folder
- optionally copies a staged unpacked extension folder into a predictable local install location
- generates the final native messaging host manifest from `native-host/host-manifest.template.json`
- writes the current-user Chrome Native Messaging registry entry for `com.automaticlanguageswitching.host`

## What `uninstall.ps1` Does

- removes the current-user Chrome Native Messaging registry entry
- removes the installed native host files unless `-KeepFiles` is specified

## Packaging Prep Scripts

- `prepare-native-host.ps1` stages published native host output into `installer/windows/payload/native-host`
- `prepare-extension.ps1` stages a clean unpacked extension folder into `installer/windows/payload/extension-unpacked`

## What Still Remains After The First Public Installer Release

- define the final install location and upgrade behavior
- improve the end-user guidance flow for loading the unpacked extension in Chrome
- add signing, versioning, and release automation if needed

## Inno Setup Build Notes

Prerequisites:

- Inno Setup installed on the build machine
- staged native host payload in `installer/windows/payload/native-host`
- staged unpacked extension payload in `installer/windows/payload/extension-unpacked`

Compile command example:

```powershell
iscc.exe installer\windows\AutomaticLanguageSwitching.iss
```

The generated installer output goes to:

- `installer/windows/output`

Important:

- the unpacked extension ID is stabilized by the `key` field in `extension/manifest.json`
- the fixed extension ID used by the native host manifest is `ajeohlkjppfeilfpjkalbleoefanigaf`
- the installer uses the staged payload folders and does not build the native host or extension itself
- the installer best-effort enables Windows per-app input settings for the current user via `SystemParametersInfo(..., SPI_SETTHREADLOCALINPUTSETTINGS, ...)`
- post-install Chrome navigation uses a bundled helper script that looks for Chrome via `where chrome`, registry App Paths, and standard install locations
- opening `chrome://extensions` is best-effort and may still require a manual fallback on some Windows setups
