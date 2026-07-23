# Coop Launcher 1.1.1

Recommended download: **`CoopLauncher-Setup-1.1.1.exe`**

This release includes the corrected Steam overlay and the new safe **Open app** workflow. The installer places Coop Launcher in the current user's local Programs folder, creates desktop and Start Menu shortcuts, and provides an uninstaller.

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
