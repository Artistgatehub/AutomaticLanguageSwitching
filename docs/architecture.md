# Architecture Overview

This document gives a high-level overview of the current AutomaticLanguageSwitching architecture. It is intended to explain how the main parts of the system fit together without duplicating the full protocol or installer documentation.

## Main Components

### Chrome extension

The Chrome extension is responsible for observing browser-side events and forwarding relevant state changes to the native host. In the current project, that primarily means:

- tracking the active Chrome tab
- notifying the native host when the active tab changes
- notifying the native host when focus returns to the current tab from another application context
- receiving restore results and warnings from the native host

### Windows Native Messaging host

The native host is the Windows-side process that keeps the in-memory tab-to-layout state for the current browser session. It is responsible for:

- receiving messages from the extension through Chrome Native Messaging
- remembering the current Windows keyboard layout for the previously active tab
- restoring the remembered layout for the newly active tab when possible
- checking the Windows per-app input setting at startup and attempting best-effort recovery if needed

### Windows keyboard layout interaction

The project depends on Windows keyboard layout APIs and related runtime state. The native host reads the current layout, validates restore attempts, and issues layout switch requests on the active Windows side of the workflow.

This part of the system is implementation-dependent and Windows-specific. It is not abstracted into a separate service layer.

### Installer and setup layer

The Windows installer prepares the local environment needed by the product. In the current setup it:

- installs the native host files
- writes the current-user Chrome Native Messaging registration
- places the unpacked extension into a predictable local folder
- best-effort enables the Windows per-app input setting

The Chrome extension is still loaded manually by the user through Chrome's `Load unpacked` flow.

## Data and Control Flow

```text
Chrome tab/focus events
        |
        v
Chrome extension
        | Native Messaging
        v
Windows native host
        |
        v
Windows keyboard layout APIs/state
```

Typical flow:

1. Chrome activates a tab, or focus returns to the current tab.
2. The extension sends a message to the native host.
3. The native host identifies the previously active tab and remembers its current layout.
4. The native host looks up the remembered layout for the current tab.
5. If a remembered layout exists, the native host attempts to restore it and reports the result back to the extension.

## Responsibilities by Component

### Extension

- Browser event detection
- Native Messaging connection lifecycle
- Lightweight status and warning logging
- Toolbar/action icon state

### Native host

- Session-only layout memory
- Restore decision logic
- Windows layout read/switch operations
- Runtime verification of the Windows per-app input setting

### Installer

- Packaging and staging
- Current-user host registration
- Local file placement for the unpacked extension
- Best-effort setup assistance for the required Windows setting

## Current Limitations and Design Constraints

- Windows only
- Chrome only
- Requires Chrome Native Messaging
- The extension is loaded manually as an unpacked extension
- The final `Load unpacked` step remains a manual Chrome action
- Layout memory is session-only and is not persisted across a full Chrome restart
- Correct behavior depends on the Windows per-app input setting being enabled

## Repository Mapping

- [`extension/`](../extension/) contains the Chrome extension source, manifest, icons, and built output
- [`native-host/`](../native-host/) contains the Windows Native Messaging host source and related project files
- [`installer/windows/`](../installer/windows/) contains the Windows installer, staging scripts, and user-facing setup instructions
- [`docs/`](./) contains protocol notes, local development notes, and architecture documentation

## Related Documents

- [`docs/protocol.md`](./protocol.md) for the Native Messaging message format
- [`docs/windows-local-dev.md`](./windows-local-dev.md) for local development notes on Windows
- [`README.md`](../README.md) for product-level installation and usage guidance
