# Coop Launcher 1.1.2

Recommended download: **`CoopLauncher-Setup-1.1.2.exe`**

## Automatic administrator compatibility repair

Some games can have a hidden Windows `RUNASADMIN` compatibility rule even when **Run as administrator** is disabled inside Coop Launcher. Steam may see the child process but cannot inject its overlay, capture it correctly, or deliver Remote Play Together input across that privilege boundary.

Version 1.1.2 fixes that case automatically:

- checks external `.exe` entries immediately before launch;
- removes only the current user's stale `RUNASADMIN` token when administrator mode is disabled;
- preserves unrelated compatibility settings such as high-DPI layers;
- leaves entries intentionally configured as administrator unchanged;
- detects machine-wide administrator rules and gives a specific instruction instead of launching an unusable Remote Play session;
- records every automatic repair in the local diagnostic log.

This release includes the complete Steam overlay, external-game, donor backup, window relaunch, diagnostics, and installer improvements from version 1.1.1.

The executable is a community build without a commercial code-signing certificate. Windows may show an unknown-publisher warning.
