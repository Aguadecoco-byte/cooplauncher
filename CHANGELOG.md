# Changelog

This changelog describes the adjustments maintained by [Aguadecoco-byte](https://github.com/Aguadecoco-byte) on top of the original [MBaliver/cooplauncher](https://github.com/MBaliver/cooplauncher) project.

## 1.1.3 - 2026-07-22

- Added an explicit guest mode that closes the donor host before accepting another person's Remote Play Together invitation.
- Warns when a game or application launched by Coop Launcher is still running, instead of terminating it and risking data loss.
- Forces the WPF application to exit with its main window so auxiliary windows cannot accidentally keep the Steam donor AppID active.
- Added clear instructions to wait until Steam releases the donor and to use a fresh invitation.

## 1.1.2 - 2026-07-22

- Automatically removes stale per-user `RUNASADMIN` compatibility rules before launching an external game configured to run unelevated.
- Preserves unrelated Windows compatibility layers such as DPI settings.
- Detects machine-wide administrator rules and explains how to disable them instead of silently launching an unshareable elevated process.
- Added registry repair smoke tests and visible status/log reporting.

## 1.1.1 - 2026-07-22

- Replaced the modal open-window picker with an in-window panel so Steam does not switch to `Desktop Placeholder` while choosing an application.
- Added a safe workflow that closes and relaunches an existing application as an unelevated direct child of the donor process.
- Removed the misleading interactive-desktop action and documented Steam Remote Play Together's game-only privacy boundary.
- Added a Windows Setup executable with desktop and Start Menu shortcuts and uninstall support.

## 1.1.0 - 2026-07-22

- Fixed distorted and clipped Steam Shift+Tab rendering with a hardware-rendered full-frame compatibility surface.
- Added external shortcut and executable profiles, arguments, working directories, icons, and per-entry administrator mode.
- Added transactional donor backup, installation, integrity verification, rollback, and restoration.
- Added atomic configuration persistence, diagnostics, crash logging, DPI awareness, and smoke tests.
- Removed the embedded SteamGridDB API key.
