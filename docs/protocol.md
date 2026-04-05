# Native Messaging Protocol v1

All messages are JSON objects wrapped with Chrome Native Messaging framing.

Common fields:

```json
{
  "version": 1,
  "type": "message_type",
  "payload": {}
}
```

## Extension -> Native Host

### `hello`

Sent once after the extension connects.

```json
{
  "version": 1,
  "type": "hello",
  "payload": {
    "extensionVersion": "0.2.0"
  }
}
```

### `tab_switched`

Sent when Chrome changes the active tab in a window.

```json
{
  "version": 1,
  "type": "tab_switched",
  "payload": {
    "previousWindowId": 1,
    "previousTabId": 41,
    "currentWindowId": 1,
    "currentTabId": 42
  }
}
```

The host uses the previous tab key to remember the layout being left, then tries to restore the remembered layout for the current tab.

### `chrome_focus_returned`

Sent when Chrome regains focus on the already-active tab after focus returns from another Windows application.

```json
{
  "version": 1,
  "type": "chrome_focus_returned",
  "payload": {
    "currentWindowId": 1,
    "currentTabId": 42
  }
}
```

### `tab_closed`

Sent when Chrome removes a tab. The host uses it to clear any in-memory remembered layout for that runtime tab key.

```json
{
  "version": 1,
  "type": "tab_closed",
  "payload": {
    "windowId": 1,
    "tabId": 42
  }
}
```

## Native Host -> Extension

### `hello_ack`

```json
{
  "version": 1,
  "type": "hello_ack",
  "payload": {
    "hostVersion": "0.2.0",
    "platform": "windows",
    "perAppInputMethodEnabled": true,
    "attemptedAutoEnable": false
  }
}
```

`perAppInputMethodEnabled` and `attemptedAutoEnable` describe the startup check for the Windows per-app input-method setting.

### `warning`

Sent when the host needs to report a non-fatal problem, such as the Windows per-app input-method setting still being disabled after the startup check.

```json
{
  "version": 1,
  "type": "warning",
  "payload": {
    "message": "Windows per-app input method setting is disabled and could not be enabled automatically.",
    "perAppInputMethodEnabled": false,
    "attemptedAutoEnable": true
  }
}
```

### `layout_restore_result`

Returned after the host handles `tab_switched` or `chrome_focus_returned`.

```json
{
  "version": 1,
  "type": "layout_restore_result",
  "payload": {
    "windowId": 1,
    "tabId": 42,
    "layoutId": "00000409",
    "result": "applied"
  }
}
```

`layoutId` is the final stable KLID the host used for the restore decision. It is normalized to an 8-digit uppercase KLID when present.

`result` values:

- `applied`
- `unavailable`
- `failed`

Meaning:

- `applied`: the host verified the expected layout or determined it was already active
- `unavailable`: the requested layout could not be resolved to an installed stable layout
- `failed`: the host attempted restore, but verification did not confirm the expected layout

### `error`

```json
{
  "version": 1,
  "type": "error",
  "payload": {
    "message": "Unsupported message type."
  }
}
```

## Current State Notes

- Layout memory is session-only. It is not persisted across a full Chrome restart.
- The protocol does not include a dev-only `assign_layout` message in the current code.
- The extension currently sends `hello`, `tab_switched`, `chrome_focus_returned`, and `tab_closed`.
