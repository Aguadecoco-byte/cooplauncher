# Coop Launcher 1.1.4

This release adds an integrated repair for a confirmed Steam Remote Play Together failure where `streaming_client.exe` closes immediately after writing `Initializing player`.

## Repair Steam

- Detects the known Millennium `wsock32.dll` bootstrapper and SteamTools/Vale `xinput1_4.dll` shim by embedded product metadata.
- Does not modify unknown DLLs.
- Refuses to run while a game, donor session, or Remote Play client is active.
- Closes Steam gracefully without force-ending it.
- Renames each conflicting DLL to a reversible backup instead of deleting it.
- Rolls back partial file changes if the repair cannot finish.
- Restarts Steam automatically when it was running before the repair.
- Displays whether a conflict is active or has already been repaired.

The repair does not modify games, Steam accounts, donor backups, or Valve-signed Steam files.
