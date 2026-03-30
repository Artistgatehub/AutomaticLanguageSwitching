# First-Release Install Flow

This document describes the intended user flow for the first public Windows release.

## What the Installer Should Do Automatically

When the user runs `installer.exe`, it should:

1. install the Windows native host files
2. register the Chrome Native Messaging host for the current user
3. place a ready-to-load unpacked Chrome extension folder in a predictable local path
4. open a local instructions page
5. open `chrome://extensions`
6. open the unpacked extension folder in Explorer

Recommended unpacked extension install path:

```text
%LOCALAPPDATA%\AutomaticLanguageSwitching\Extension
```

## What the User Still Needs To Do Manually

Normal Chrome users still need to complete the final Chrome steps themselves:

1. enable Developer mode on `chrome://extensions`
2. click `Load unpacked`
3. select the prepared extension folder

## Why Manual Chrome Steps Still Remain

For a normal public release, the unpacked extension loading step is still a user action in Chrome. This repository does not assume Chrome Web Store distribution, enterprise policy installation, or a fully silent extension install path for normal end users.

## Stable Extension ID

The unpacked extension now uses a fixed manifest `key`, so its extension ID remains stable across install locations. That keeps the native messaging `allowed_origins` entry aligned with the installed extension.

## Installer Guidance Recommendation

The installer should clearly show:

- where the unpacked extension folder was placed
- that the native host was installed automatically
- that Chrome still requires the final `Load unpacked` step
- the exact folder the user should select
