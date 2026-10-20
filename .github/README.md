# Rescan only the refreshed artists' folders

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`. Reviewed in [#19](https://github.com/chodeus/Lidarr/pull/19).

Refreshing an artist with rescan on used to scan every root folder. Only newly added artists had their scan limited to their own folders.

- A refresh of chosen artists (the artist page's Refresh & Scan, or a bulk refresh) now rescans only those artists' folders. It is quicker, and a problem file under another artist can't affect it.
- A full refresh (the scheduled task, or refreshing all artists) still scans the root folders, so files outside any artist folder are still found.
- If a chosen artist was merged into another, or deleted, during the refresh, the root folders are scanned instead, since its files may now belong to another artist. The gone artist is left out of the scan, which would otherwise fail for every artist on it.
- `RescanArtists` is private, so the change stays inside `RefreshArtistService`.

Tests: a selected refresh pushes a rescan of the artist's folder, a full refresh pushes a rescan of the root folders, and a refresh with an artist merged away pushes a rescan of the root folders. `RefreshArtistRescanFixture` runs the merge through the real `ArtistService` and repository over the test database, and checks the scan's artists can all be read back. Four mutations of the merged-away check each fail a test.

## Summary by CodeRabbit

* **Bug Fixes**
  * Manual refreshes of selected artists now rescan their folders when all requested artists are found, avoiding scans of unrelated folders.
  * If a requested artist has been merged away, the refresh rescans root folders instead. Full-library manual refreshes continue to scan root folders, ensuring collection-wide coverage when the selected set cannot be fully resolved.
