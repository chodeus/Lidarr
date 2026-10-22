# Rescan only the refreshed artists' folders

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`. Reviewed in [#19](https://github.com/chodeus/Lidarr/pull/19).

Refreshing an artist with rescan on used to scan every root folder. Only newly added artists had their scan limited to their own folders.

- A refresh of chosen artists (the artist page's Refresh & Scan, or a bulk refresh) now rescans only those artists' folders, at their paths as stored when the refresh ends, so an artist moved after the refresh saved it is scanned where it now is. It is quicker, and a problem file under another artist can't affect it. Files outside every artist folder (loose or unmapped files) are found by a full refresh, or when a refresh of chosen artists falls back to the root folders, as described below.
- A move saved while the refresh is fetching the artist's metadata is overwritten by the refresh's own save on `develop`; `fix/refresh-keeps-concurrent-changes`, later in the custom build, keeps it.
- An artist whose track files sit outside its folder, such as after its path was edited without moving them, gets the root folders scanned, since only that cleans up its files gone from disk. That counts the files of every release, including ones that aren't monitored, which `GetFilesByArtist` leaves out.
- A chosen artist outside every root folder, such as after its root folder was removed, is left out of the scan with a warning: the scan stops at such a folder, so the other artists would go unscanned. With nothing left to scan, no rescan is queued.
- A full refresh (the scheduled task, or refreshing all artists) still scans the root folders, so files outside any artist folder are still found. Its scan leaves out artists merged away or deleted while it ran.
- If a chosen artist was merged into another, or deleted, during the refresh, the root folders are scanned instead, since its files may now belong to another artist. This holds for newly added artists too. The gone artist is left out of the scan, and so is a repeat of an artist named twice, since the scan reads its artists back by id and fails on either.
- On `develop`, a refresh that fails after such a merge, or changes the merged artist's auto-tags, still stops before the scan; `fix/refresh-keeps-concurrent-changes`, later in the custom build, fixes that.
- `RescanArtists` is private, so the change stays inside `RefreshArtistService`.

Tests: a selected refresh pushes a rescan of the artist's folder, a full refresh pushes a rescan of the root folders, and a refresh with an artist merged away pushes a rescan of the root folders. `RefreshArtistRescanFixture` runs the merge through the real `ArtistService` and repository over the test database, and checks the scan's artists can all be read back; a new artist merged away, an artist named twice, one moved during its refresh, and a full refresh with an artist merged away run the same way. `RefreshArtistScopedScanFixture` (re-audit, 2026-10) runs the pushed rescan through the real `DiskScanService`, root folders and media files over the test database: another chosen artist is still scanned beside one outside every root folder, an artist with files outside its folder gets the root folders scanned and its file gone from disk removed, one whose files are inside gets only its folder, and nothing is queued when no folder is left. Twelve mutations each fail a test.

## Summary by CodeRabbit

* **Bug Fixes**
  * Manual refreshes of selected artists now rescan their folders when all requested artists are found, avoiding scans of unrelated folders.
  * If a requested artist has been merged away, the refresh rescans root folders instead. Full-library manual refreshes continue to scan root folders, ensuring collection-wide coverage when the selected set cannot be fully resolved.
