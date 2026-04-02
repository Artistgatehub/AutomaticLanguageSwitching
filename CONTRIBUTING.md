# Contributing

Contributions are welcome. This project is still relatively small, so the most helpful changes are usually focused fixes, clear documentation improvements, and practical refinements to the current Windows + Chrome workflow.

## Ways to Contribute

Useful contributions include:

- bug reports with clear reproduction steps
- documentation improvements
- installer and setup improvements
- Chrome extension fixes
- native host or Windows layout handling fixes
- logging, troubleshooting, and readability improvements

## Before Making Changes

Before opening a pull request:

- read the main [README](./README.md)
- check relevant docs in [docs/](./docs/)
- keep the change focused on one problem
- avoid unrelated refactors

If you are changing behavior, update the relevant documentation in the same pull request when appropriate.

## Development Areas

- [`extension/`](./extension/) Chrome extension source, manifest, icons, and built output
- [`native-host/`](./native-host/) Windows Native Messaging host source
- [`installer/windows/`](./installer/windows/) installer scripts, payload staging, and user-facing setup files
- [`docs/`](./docs/) protocol notes, architecture notes, and development documentation

## General Guidelines

- Prefer small, focused pull requests.
- Describe the problem and the fix clearly.
- Preserve current Windows-only and Chrome-only assumptions unless you are intentionally working on project scope.
- Avoid speculative features or architectural redesigns unless they are discussed first.
- Keep installer changes practical and honest about what is still manual.
- Update `README.md`, `CHANGELOG.md`, or other docs when user-visible behavior changes.

## Testing and Validation

There is no large automated test suite in the repository today, so manual validation matters.

When relevant, verify:

- the extension still loads correctly in Chrome
- native host communication still works
- core tab/layout behavior still works during the current Chrome session
- focus return from another Windows application still restores the expected layout
- installer-related changes still work correctly on Windows

If you change build or packaging behavior, include the command you ran and the result in your pull request summary when helpful.

## Issues and Pull Requests

For bug reports:

- include clear reproduction steps
- describe expected behavior and actual behavior
- include logs or screenshots when they help

For pull requests:

- keep scope focused
- summarize what changed and why
- note any manual validation you performed
- mention any known limitations or follow-up work

## Notes

Future additions that may be useful:

- issue templates
- a pull request template

