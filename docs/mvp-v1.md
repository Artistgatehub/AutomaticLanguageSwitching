# MVP v1

## Revised Scope

MVP v1 is a single end-to-end vertical slice for Windows only.

Included:
- Detect active Chrome tab changes.
- Send tab activation events from a Chrome extension to a native Windows host through Native Messaging.
- Keep per-tab keyboard layout assignments in memory only.
- Restore an assigned layout when that tab becomes active.
- Fail softly if the requested layout is not installed.

Excluded:
- Automatic observation of user layout changes.
- Persistence across browser or host restarts.
- macOS support.
- Popup or options UI.
- Broad installer or packaging work beyond the minimum needed to register the native host during development.

Runtime key:
- `windowId + tabId`

State model:
- In-memory only.

## Minimal Architecture

There are only two runtime components:

1. Chrome extension
   - Runs a background service worker.
   - Listens for `chrome.tabs.onActivated`.
   - Maintains a Native Messaging connection.
   - Sends `hello` once on connect.
   - Sends `tab_activated` on active-tab changes.
   - Can send `assign_layout` during development to seed the in-memory mapping.

2. Native host (`.NET 8`, Windows)
   - Reads and writes Native Messaging frames on stdin/stdout.
   - Keeps an in-memory map from `(windowId, tabId)` to keyboard layout ID.
   - On `tab_activated`, looks up the assignment and tries to switch to it.
   - Only switches to layouts that are already installed.
   - Logs soft failures instead of changing system settings.

Flow:

1. Extension connects to native host.
2. Extension sends `hello`.
3. User activates a tab.
4. Extension sends `tab_activated`.
5. Native host looks up `(windowId, tabId)`.
6. If a layout is assigned and installed, host switches to it.
7. If not assigned or unavailable, host does nothing and logs the reason.

## Demo Note

Because MVP v1 does not observe manual layout changes and has no UI, the demo needs a way to create an assignment. The minimal path is a dev-only `assign_layout` message sent from the extension background context.
