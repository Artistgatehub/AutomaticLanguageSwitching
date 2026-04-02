# AutomaticLanguageSwitching

AutomaticLanguageSwitching is a Windows-only project that remembers the active Windows keyboard layout for each Chrome tab and restores that layout automatically when you return to the tab.

It uses a Chrome extension together with a Windows Native Messaging host. The extension detects tab switches, and the native host reads and restores the current Windows keyboard layout.

## Quick Start

1. Download `AutomaticLanguageSwitching-Setup.exe` from GitHub Releases.
2. Run the installer.
3. Read the local instructions opened by the installer and use the opened Extension folder in Explorer.
4. Open `chrome://extensions` manually in Chrome.
5. Enable Developer mode, click Load unpacked, and select:
   `%LOCALAPPDATA%\AutomaticLanguageSwitching\Extension`

## Installation

1. Download `AutomaticLanguageSwitching-Setup.exe` from the GitHub Releases page.
2. Run the installer.
3. The installer:
   1. installs the Windows native host
   2. tries to enable the Windows setting to use a different input method for each app window
   3. opens local installation instructions
   4. opens the installed Extension folder in Explorer
4. Open `chrome://extensions` manually in Chrome.
5. In Chrome:
   1. Enable Developer mode.
   2. Click Load unpacked.
   3. Select:
      `%LOCALAPPDATA%\AutomaticLanguageSwitching\Extension`

The installer sets up the Windows native host and places the Chrome extension files locally, but the final Chrome Load unpacked step is still manual.
If layout restore is still inconsistent when returning from other Windows apps, confirm this Windows setting is enabled:
`Settings > Time & language > Typing > Advanced keyboard settings > Let me use a different input method for each app window`

## Current Status

The core feature is currently working.

Verified working layouts:

- `04090409` = English
- `04190419` = Russian
- `04220422` = Ukrainian

## Features

- Per-tab keyboard layout memory for Chrome tabs
- Automatic layout restore when switching back to a tab
- Windows native host integration through Chrome Native Messaging
- In-memory tab layout tracking during the current session
- Basic diagnostic logging for development and troubleshooting

## Known Limitations

- Windows-only
- Chrome-only
- Chrome still requires the final Load unpacked step manually

## How It Works

At a high level:

1. The Chrome extension watches active tab changes.
2. On each tab switch, the extension sends a Native Messaging event to the Windows host.
3. The native host remembers the current Windows keyboard layout for the tab being left.
4. The native host looks up the remembered layout for the tab being entered.
5. If a layout is known for that tab, the host restores it.

## Architecture

- Chrome extension: tracks tab activation and sends messages to the native host
- Native Messaging host: runs on Windows, reads the current keyboard layout, remembers per-tab layout state, and restores layouts on tab switches

## Repository Structure

- [`extension/`](./extension) Chrome extension source files for tab-switch tracking
- [`native-host/`](./native-host) Windows native host source, manifest template, and .NET project files
- [`docs/`](./docs) project notes, protocol details, and local development documentation
- [`README.md`](./README.md) repository overview

## Local Development

High-level local workflow:

1. Build the Windows native host.
2. Create a local native host manifest from the template and point it to your built executable.
3. Register the native host manifest in Windows for Chrome.
4. Load the unpacked extension from the `extension/` directory.
5. Reload Chrome components as needed and test tab switching.

See the `docs/` folder for current development notes.

## Roadmap

Planned future work, not all implemented yet:

- Cleaner local developer onboarding
- Persistence across restart
- Broader testing and validation
- Potential user-facing configuration later

## Distribution

The project is currently distributed through GitHub Releases.

- Download `AutomaticLanguageSwitching-Setup.exe` from GitHub Releases
- Source code and development progress live in this repository
- No Chrome Web Store distribution is planned right now
