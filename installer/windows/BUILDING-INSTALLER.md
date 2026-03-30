# Building the First Installer

## Prerequisites

- Inno Setup installed on the build machine
- native host payload already staged in `installer/windows/payload/native-host`
- unpacked extension payload already staged in `installer/windows/payload/extension-unpacked`

## Required Build-Time Value

Before compiling, you must provide:

- `AppExtensionId`

This value is used in the generated native messaging host manifest `allowed_origins` entry.

## Compile Example

```powershell
iscc.exe /DAppExtensionId=PASTE_EXTENSION_ID_HERE installer\windows\AutomaticLanguageSwitching.iss
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
