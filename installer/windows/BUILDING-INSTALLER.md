# Building the First Installer

## Prerequisites

- Inno Setup installed on the build machine
- native host payload already staged in `installer/windows/payload/native-host`
- unpacked extension payload already staged in `installer/windows/payload/extension-unpacked`

## Stable Extension ID

The unpacked extension ID is stabilized by the `key` field in `extension/manifest.json`.

The fixed ID is:

- `ajeohlkjppfeilfpjkalbleoefanigaf`

## Compile Example

```powershell
iscc.exe installer\windows\AutomaticLanguageSwitching.iss
```

## What the Installer Includes

- native host files under `%LOCALAPPDATA%\AutomaticLanguageSwitching\NativeHost`
- unpacked extension files under `%LOCALAPPDATA%\AutomaticLanguageSwitching\Extension`
- generated native host manifest under the native host install folder
- current-user Chrome Native Messaging registration
- a local instruction file for the user

## What the Installer Does Not Automate

- enabling Chrome Developer mode
- clicking `Load unpacked`
- selecting the unpacked extension folder in Chrome
