# Windows Local Development Setup

## `layoutId` Format

The native host accepts keyboard layouts in Windows KLID format:

- 8-character hexadecimal string
- uppercase in docs and on the wire
- examples: `00000409`, `00000422`, `00010409`

Remembered layouts are stored in memory as normalized stable KLIDs for the current Chrome session only.

The native host treats the runtime-loaded keyboard layout list from `GetKeyboardLayoutList` as the primary source of truth because it reflects layouts currently loaded in the Windows session. If that call returns no layouts, the host falls back to `HKCU\Keyboard Layout\Preload` plus `Substitutes`, which is a reasonable approximation but not a perfect runtime guarantee.

## Native Host Manifest

Start from:

- `native-host/host-manifest.template.json`

For local development:

1. Build the native host.
2. Confirm the executable path in the manifest `path` field.
3. Keep the checked-in extension `key` field unchanged so the unpacked extension ID stays stable.

## Manifest Location for Chrome on Windows

Chrome looks up the host through the registry first. The registry value points to the manifest file path.

A common local development location is:

```text
<repo-root>\native-host\com.automaticlanguageswitching.host.json
```

The manifest file can live anywhere on disk as long as the registry points to its absolute path.

## Minimum Registry Entry

Create this key:

```text
HKEY_CURRENT_USER\Software\Google\Chrome\NativeMessagingHosts\com.automaticlanguageswitching.host
```

Set its default value to the absolute path of the manifest file, for example:

```text
<repo-root>\native-host\com.automaticlanguageswitching.host.json
```

PowerShell example:

```powershell
$key = "HKCU:\Software\Google\Chrome\NativeMessagingHosts\com.automaticlanguageswitching.host"
$repoRoot = "C:\path\to\AutomaticLanguageSwitching"
$manifestPath = Join-Path $repoRoot "native-host\com.automaticlanguageswitching.host.json"
New-Item -Path $key -Force | Out-Null
Set-ItemProperty -Path $key -Name "(default)" -Value $manifestPath
```

If `Set-ItemProperty` does not set the unnamed default value correctly in your shell, use `reg.exe`:

```powershell
reg add "HKCU\Software\Google\Chrome\NativeMessagingHosts\com.automaticlanguageswitching.host" /ve /t REG_SZ /d "C:\path\to\AutomaticLanguageSwitching\native-host\com.automaticlanguageswitching.host.json" /f
```

## Short Local Test Checklist

1. Build the native host executable.
2. Copy `host-manifest.template.json` to `com.automaticlanguageswitching.host.json`.
3. Replace the manifest `path` with the actual built `.exe` path if needed.
4. Load the unpacked extension from the `extension/` directory in Chrome.
5. Keep the manifest `allowed_origins` entry aligned with the stable unpacked extension ID from the checked-in manifest key.
6. Create the `HKCU\Software\Google\Chrome\NativeMessagingHosts\com.automaticlanguageswitching.host` registry entry pointing to that manifest.
7. Reload the extension.
8. Open the service worker console and verify `hello_ack` arrives from the host.
9. Open two normal web tabs, type with different layouts in each tab, and switch back and forth.
10. Observe `layout_restore_result` messages and confirm the typed input matches the expected layout in each tab.

## WinAPI Notes

The host uses this path on Windows:

- `GetForegroundWindow` and `GetWindowThreadProcessId` to identify the active Chrome-side input context
- `GetKeyboardLayout` and `GetKeyboardLayoutNameW` to observe the active layout
- `GetKeyboardLayoutList` to enumerate layouts currently loaded in the session
- `LoadKeyboardLayoutW` to resolve the target KLID into an `HKL`
- `ActivateKeyboardLayout` plus `SendMessageTimeout(..., WM_INPUTLANGCHANGEREQUEST, ...)` during restore

The switch request is sent to the foreground window because the native host process does not own Chrome's UI thread. As a small MVP safeguard, the host only sends the request if the current foreground process is still `chrome.exe`.
