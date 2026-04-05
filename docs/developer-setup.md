# Developer Setup

This document is a short guide for local development and manual validation. It is intended for contributors and maintainers working on the repository locally, especially on Windows.

## Project Areas

- [`extension/`](../extension/) contains the Chrome extension source, manifest, icons, and TypeScript build setup
- [`native-host/`](../native-host/) contains the Windows Native Messaging host source and .NET project
- [`installer/windows/`](../installer/windows/) contains installer scripts, payload staging, and Windows packaging files
- [`docs/`](./) contains architecture notes, protocol notes, and supporting development documentation

## High-Level Local Workflow

Typical development flow:

1. Make the change in the relevant area of the repository.
2. Rebuild the component you touched.
3. Reload or restage that component as needed.
4. Manually verify the tab/layout behavior on Windows.

In practice:

- extension changes usually require rebuilding the TypeScript output and reloading the unpacked extension in Chrome
- native host changes usually require rebuilding the host and re-testing the Native Messaging connection
- installer changes should be validated carefully on Windows because they affect file placement, host registration, or the unpacked extension flow

## Build and Packaging Notes

Commands that are clearly supported by the repository today:

- Extension build:
  ```powershell
  cd extension
  npm run build
  ```
- Native host build:
  ```powershell
  cd native-host
  dotnet build
  ```

For installer work:

- review [`installer/windows/README.md`](../installer/windows/README.md)
- review [`installer/windows/BUILDING-INSTALLER.md`](../installer/windows/BUILDING-INSTALLER.md)

Those docs are the best source of truth for staging payloads and compiling the Windows installer.

## Manual Validation

There is no large automated test suite in the repository today, so manual validation is expected.

When relevant, verify:

- the unpacked extension still loads correctly in Chrome
- the native host still connects successfully
- switching between tabs still restores the expected keyboard layout
- returning from another Windows application back to Chrome still restores the expected layout
- layout memory remains session-only across the current Chrome runtime
- installer changes still produce a working Windows setup flow

If your change affects user-visible behavior, also verify that the relevant docs still match the actual flow.

## Related Documents

- [`README.md`](../README.md) for product overview and installation flow
- [`CONTRIBUTING.md`](../CONTRIBUTING.md) for contribution expectations
- [`docs/architecture.md`](./architecture.md) for a high-level system overview
- [`docs/protocol.md`](./protocol.md) for the Native Messaging protocol
- [`docs/windows-local-dev.md`](./windows-local-dev.md) for Windows-specific local development notes

## Current Protocol Reminder

The current Native Messaging flow uses `hello`, `tab_switched`, `chrome_focus_returned`, and `tab_closed` from the extension, plus `hello_ack`, `warning`, `layout_restore_result`, and `error` from the host. Use [`docs/protocol.md`](./protocol.md) as the source of truth for the current message shapes.
