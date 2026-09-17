# Project live synchronization

## Problem

After selecting a project in Quest, newly added furniture was not visible until the user entered the project number again. Re-running project selection also cleared placed furniture, making the workflow disruptive.

## Changes

- Added `GET /api/projects/{id}/revision` to return a SHA-256 revision of the project's furniture JSON.
- `ModelLoader` keeps the selected project ID and checks the revision every 5 seconds.
- The full `/models` response is downloaded only when the revision changes.
- A refresh updates the furniture selection list without deleting furniture already placed in the MR room.
- Empty projects remain selected and continue waiting for updates.
- Added public `UI_RefreshProject()` for an optional manual refresh button.
- Revision checks pause while a screenshot is being captured.

## Unity setup

1. Select the object containing `ModelLoader`.
2. Set **Project Sync Interval Seconds**. The recommended value is `5`.
3. Optionally connect a menu button's `OnClick` event to `ModelLoader.UI_RefreshProject`.

## Verification

1. Start the app and select a project once.
2. Add furniture to the same project on the website.
3. Wait up to the configured interval.
4. Confirm that the selection list updates and already placed furniture remains in the room.
5. Disconnect Wi-Fi briefly and confirm MR interaction continues; reconnect and confirm a later revision check recovers.

## Design note

The revision request is only a small JSON payload. GLB files are not downloaded by the polling loop. SignalR can be added later for faster notifications, while this endpoint should remain as a reconnection fallback.
