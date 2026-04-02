# First-Release Install Flow

This document describes the intended user flow for the first public Windows release.

## What the Installer Does Automatically

When the user runs `AutomaticLanguageSwitching-Setup.exe`, it:

1. install the Windows native host files
2. register the Chrome Native Messaging host for the current user
3. best-effort enable the Windows setting to use a different input method for each app window for the current user
4. place a ready-to-load unpacked Chrome extension folder in a predictable local path
5. open a local instructions page
6. open the unpacked extension folder in Explorer

Recommended unpacked extension install path:

```text
%LOCALAPPDATA%\AutomaticLanguageSwitching\Extension
```

## What the User Still Needs To Do Manually

Normal Chrome users still need to complete the final Chrome steps themselves:

1. open `chrome://extensions` manually
2. enable Developer mode on `chrome://extensions`
3. click `Load unpacked`
4. select the prepared extension folder
5. if Windows still does not keep separate input methods per app window, enable that setting manually in:
   `Settings > Time & language > Typing > Advanced keyboard settings`

## Why Manual Chrome Steps Still Remain

For a normal public release, the unpacked extension loading step is still a user action in Chrome. This repository does not assume Chrome Web Store distribution, enterprise policy installation, or a fully silent extension install path for normal end users.

## Stable Extension ID

The unpacked extension now uses a fixed manifest `key`, so its extension ID remains stable across install locations. That keeps the native messaging `allowed_origins` entry aligned with the installed extension.

## Installer Guidance Recommendation

The installer should clearly show:

- where the unpacked extension folder was placed
- that the native host was installed automatically
- that the user must open `chrome://extensions` manually
- that Chrome still requires the final `Load unpacked` step
- the exact folder the user should select
