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

### `tab_activated`

Sent whenever Chrome activates a tab.

```json
{
  "version": 1,
  "type": "tab_activated",
  "payload": {
    "windowId": 1,
    "tabId": 42
  }
}
```

### `assign_layout`

Dev-only message used to seed an in-memory mapping for the current MVP.

```json
{
  "version": 1,
  "type": "assign_layout",
  "payload": {
    "windowId": 1,
    "tabId": 42,
    "layoutId": "00000409"
  }
}
```

`layoutId` is the Windows keyboard layout identifier string to restore later.
For MVP v1, the accepted format is an 8-character uppercase hexadecimal KLID, for example `00000409` (`en-US`) or `00000422` (`uk-UA`).
The native host normalizes incoming values to uppercase and rejects any other format.

### `tab_closed`

Sent when Chrome removes a tab. The native host uses it only to clear the in-memory assignment for that runtime tab key.

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
    "platform": "windows"
  }
}
```

### `layout_restore_result`

Returned after handling `tab_activated` when an assignment exists.

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

`result` values:
- `applied`
- `unavailable`
- `failed`

Meaning:
- `applied`: the host sent the switch request to the current foreground window
- `unavailable`: the requested `layoutId` is not currently installed for the Windows user profile, or the format is invalid
- `failed`: the layout is installed but Windows did not accept the switch request

### `assign_layout_ack`

```json
{
  "version": 1,
  "type": "assign_layout_ack",
  "payload": {
    "windowId": 1,
    "tabId": 42,
    "layoutId": "00000409"
  }
}
```

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
