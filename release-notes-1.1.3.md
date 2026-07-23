# Coop Launcher 1.1.3

This release prevents a Coop Launcher host session from interfering when the same PC wants to join someone else's Steam Remote Play Together session.

## Changes

- New **Guest** button that closes the donor session cleanly.
- Detects games and applications launched during the current session and asks the user to close them first.
- The application now always exits when its main window closes, ensuring Steam can release the donor AppID.
- Clear guidance to wait until the donor stops showing as running in Steam and accept a fresh invitation.

The Remote Play connection itself is still handled by Steam's official `streaming_client.exe`; Coop Launcher does not replace or modify that component.
