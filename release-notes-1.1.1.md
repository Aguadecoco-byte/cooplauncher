# Coop Launcher 1.1.1

Recommended download: **`CoopLauncher-Setup-1.1.1.exe`**

This release includes the corrected Steam overlay and the new safe **Open app** workflow. The installer places Coop Launcher in the current user's local Programs folder, creates desktop and Start Menu shortcuts, and provides an uninstaller.

## Changes from the original project / Cambios respecto al original

This maintained fork was customized by [Aguadecoco-byte](https://github.com/Aguadecoco-byte) while preserving the original concept and GPL-3.0 license from [MBaliver/cooplauncher](https://github.com/MBaliver/cooplauncher).

- Corrected the clipped and distorted **Shift+Tab** interface so the complete Remote Play Together panel and second-player controller slots remain visible.
- Added desktop shortcuts, executables, scripts, URLs, arguments, working directories, custom icons, and an optional per-game administrator setting.
- Added an integrated window picker plus controlled relaunching of an already-open application as a child of the Steam donor process.
- Replaced the misleading desktop/window sharing option with an honest explanation of Steam's `Host busy` privacy boundary. Arbitrary interactive desktop capture is not exposed by the public Steam Remote Play API.
- Reworked donor installation with verified backups, SHA-256 integrity checks, automatic rollback, safe restoration, and running-process protection.
- Added atomic configuration saves, corrupt-file recovery, diagnostic and crash logs, Steam library discovery improvements, per-monitor DPI support, and a landscape overlay-friendly layout.
- Removed the embedded SteamGridDB credential; users can provide their own `STEAMGRIDDB_API_KEY`.
- Added smoke and donor integration tests, automated release builds, and a Windows `Setup.exe` with desktop/Start Menu shortcuts and uninstall support.

See the [complete source comparison](https://github.com/MBaliver/cooplauncher/compare/main...Aguadecoco-byte:cooplauncher:main) for the exact file-by-file changes.

## Highlights

- Fixed clipped/distorted Shift+Tab overlay and controller assignment UI.
- Added external desktop shortcuts and executable profiles.
- Added verified donor backup, rollback, and restoration.
- Added an in-window application picker that no longer triggers `Desktop Placeholder` while it is open.
- Added graceful relaunching of an existing app as an unelevated child of the donor, allowing Steam to associate it with the Remote Play Together session.
- Added diagnostic logs, atomic configuration, DPI support, and smoke tests.

## Important limitation

Steam Remote Play Together intentionally blocks guest input when the host leaves the donor's game process for the Windows desktop. Coop Launcher therefore does not advertise interactive desktop sharing. Use Steam Link, Sunshine/Moonlight, or another remote-desktop product for that purpose.

## Verification

The release was built and tested on Windows 11 x64 with .NET 8. The main build completed with 0 warnings and 0 errors, smoke tests passed, and a tracked-window integration test confirmed Steam overlay injection and AppID association.

The executable is a community build without a commercial code-signing certificate. Windows may show an unknown-publisher warning.
