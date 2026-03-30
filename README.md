# AutomaticLanguageSwitching

AutomaticLanguageSwitching is a Windows-only project that remembers the active Windows keyboard layout for each Chrome tab and restores that layout automatically when you return to the tab.

It uses a Chrome extension together with a Windows Native Messaging host. The extension detects tab switches, and the native host reads and restores the current Windows keyboard layout.

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

## Current Limitations

- Windows-only
- Chrome-only
- No persistence across restart yet
- No settings UI yet
- No packaged installer yet
- Local setup is still manual for development

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

- Better setup and installation workflow
- Cleaner local developer onboarding
- Persistence across restart
- Broader testing and validation
- Potential user-facing configuration later

## Distribution Plan

The current plan is GitHub-first distribution.

- Source code and development progress will live on GitHub first
- A packaged `installer.exe` is planned later
- No Chrome Web Store distribution is planned right now
