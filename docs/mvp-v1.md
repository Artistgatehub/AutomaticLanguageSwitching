# MVP v1

This note is kept as a historical scope reference for the earliest end-to-end Windows prototype. It is not the source of truth for the current runtime protocol.

## Revised Scope

MVP v1 established a single end-to-end vertical slice for Windows only.

Included:
- Detect active Chrome tab changes.
- Send tab and focus-return events from a Chrome extension to a native Windows host through Native Messaging.
- Keep per-tab keyboard layout memory in-process for the current Chrome session.
- Restore a remembered layout when that tab becomes active again.
- Fail softly if the requested layout is unavailable or Windows does not verify the restore.

Excluded:
- Persistence across browser or host restarts.
- macOS support.
- Popup or options UI.
- Chrome Web Store distribution.

Runtime key:
- `windowId + tabId`

State model:
- In-memory only for the current Chrome session.

## Current Baseline Compared With The Original MVP

The current code keeps the same core product boundary, but the live protocol now uses:

- `hello`
- `tab_switched`
- `chrome_focus_returned`
- `tab_closed`

The host responds with:

- `hello_ack`
- `warning`
- `layout_restore_result`
- `error`

See [`docs/protocol.md`](./protocol.md) for the actual current protocol and [`docs/architecture.md`](./architecture.md) for the current system view.
