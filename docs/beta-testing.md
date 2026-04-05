# Closed Beta Testing Guide

This guide is for early testers of AutomaticLanguageSwitching who want to try the current Windows build and send useful feedback.

## Who This Beta Is For

- people who actively switch between two or more keyboard layouts while using Chrome
- Windows 10 or Windows 11 users
- testers comfortable loading an unpacked Chrome extension after running the installer

## Supported Environment

- Windows only
- Chrome-based flow
- current release installer from GitHub Releases
- unpacked extension loaded manually in Chrome

## Install For Beta Testing

1. Download the latest release installer from GitHub Releases.
2. Run the installer.
3. Open `chrome://extensions`.
4. Enable `Developer mode`.
5. Click `Load unpacked`.
6. Select `%LOCALAPPDATA%\AutomaticLanguageSwitching\Extension`.
7. If needed, confirm the Windows setting `Let me use a different input method for each app window` is enabled.

## Short Test Checklist

1. Open tab A and use English.
2. Open tab B and use Ukrainian.
3. Switch back to tab A.
4. Switch back to tab B.
5. Switch from Chrome to another app and then back to Chrome.
6. Type 2-3 characters in each case to verify the real input language, not just the tray indicator.

## Known Limitations

- Windows only
- Chrome only
- the extension still uses the manual `Load unpacked` flow
- layout memory is session-only and is not persisted across a full Chrome restart
- Chrome Web Store distribution is not available

## How To Report A Problem

When reporting a bug, send:

- Windows version
- app / release version
- expected behavior
- actual behavior
- extension console log
- `%LOCALAPPDATA%\AutomaticLanguageSwitching\NativeHost\als-native-host.log`

If possible, also include:

- the smallest reproduction steps
- whether the problem happens every time or only sometimes
- whether it happens on Windows 10, Windows 11, or both

## Fast Log Checklist

1. Reproduce the issue once.
2. Copy the extension console log.
3. Copy `%LOCALAPPDATA%\AutomaticLanguageSwitching\NativeHost\als-native-host.log`.
4. Include the release version you installed.
5. Describe what you expected to happen and what actually happened.
