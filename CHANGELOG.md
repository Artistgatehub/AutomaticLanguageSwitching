# Changelog

All notable changes to this project will be documented in this file.

The format is inspired by [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Version entries below are based on repository history and current project documentation. Dates are omitted where they are not clearly established from local repository state.

## [Unreleased]

- No unreleased changes documented yet.

## [0.2.0]

### Added

- Runtime re-check and best-effort self-healing for the Windows per-app input setting used by the project.
- Clearer native host and extension logging for connection state, layout restore flow, and Windows setting recovery.
- Project branding assets for the extension and installer.

### Changed

- Normalized user-visible versioning to `0.2.0` across the extension, native host, and installer.
- Improved README and installation documentation for the public GitHub release flow.
- Refined installer packaging and staged payload flow for the native host and unpacked extension.

### Fixed

- Restored the remembered layout when returning from another Windows application to the active Chrome tab.
- Improved unpacked extension icon handling for runtime active/inactive icon switching.

## [0.1.0]

### Added

- First public Windows installer flow for installing the native host and preparing the unpacked Chrome extension.
- Stable unpacked extension ID configuration for the Chrome extension and native host integration.

### Changed

- Documentation updated for the first public release and manual `Load unpacked` installation flow.

### Notes

- Chrome installation remained unpacked and manual; the final `Load unpacked` step was required.

