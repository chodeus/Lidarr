# Fixed: Keep an album's files until the import that replaces them is in

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`. It is stacked on `fix/unmapped-duplicates` and merges after it. Reviewed in [#54](https://github.com/chodeus/Lidarr/pull/54).

Every download import runs `ImportApprovedTracks` with `replaceExisting`. Develop deletes the album's track files (to the recycle bin) and switches the monitored release before any new file moves in. When none of the new files import (the destination exists, a permission error, a transfer that fails, a database error while saving the rows), the album is left with no files, on a release that has none.

This keeps the old files until the new ones are in:
- Before `RemoveExistingTrackFiles`, the album's files, and the lyrics, metadata and other extras linked to them, move to a hidden `.lidarr-replaced/<albumId>/` folder beside them. Disk scans skip dot folders. `RemoveExistingTrackFiles` is unchanged: it finds the files gone and deletes only their rows, recorded as an upgrade.
- The release switch waits until a track of the album has imported and the new rows are saved.
- At least one track imported: the release switches, and the files waiting aside go to the recycle bin, together with anything an interrupted import of the album left in the folders the new files went to.
- No track imported, or saving the new rows failed: the files move back, their rows and track links are re-created, the extras' rows are re-inserted, and the "Track File Deleted" history entries the removal wrote are deleted through `IHistoryRepository`; `IHistoryService` is as on `develop`. A file that can't go back stays aside with its extras and is logged; the others still go back.
- When another album's import now holds a file's old path, that album keeps its file, and the old file goes to the recycle bin with its extras, as develop's upgrade would have sent it.
- If switching the release or recycling fails after the import, the error is logged and the import stands; the old files stay aside.
- Moving aside, putting back and recycling live in a new `TrackFileReplacementService` (`IReplaceTrackFiles`), which `ImportApprovedTracks` calls. Develop's 17-argument constructor is kept for plugins built against it; built that way, a put-back album loses its extras and keeps its deletion history, as on develop.

Two side effects:
- With Kodi album metadata on, an `album.nfo` written because the import created a new album folder describes the previously monitored release until the next rescan, since the switch now comes after the files move.
- Paths are matched exactly, as Lidarr does on Linux. On storage that ignores case, an old path that another album's import holds under another spelling isn't seen as held: that album's new file goes to the recycle bin and the old file takes its place.

Tests:
- `ImportApprovedTracksRollbackFixture`: the move aside, the deferred switch and the release the TrackImported events report, recycling once a track imports, putting back when none does, a failed transfer, a later album failing to move aside, a partial delete, leftovers from an interrupted import, a failed switch, a failed recycle, and an upgrade through the real upgrade service.
- `ImportApprovedTracksRealServicesRollbackFixture`: the same paths through the real media file, track, release and lyric services, saving the new rows failing, a refresh that writes releases during or before the import, an album without files, gapped disc folders, and two monitored releases.
- `ImportApprovedTracksRollbackEdgeFixture`: another album holding an old path, lyrics following a recycled file and waiting with one that can't go back, the history entries (through the real history service, keeping other entries), a failed quality profile load, an album added after identification, the release reported when switching fails, a file that can't go back or can't be looked up, a later album's lookup failing, linking the new files failing, removing their rows failing, both failing, removing the empty backup folder failing, an album's own files going back when a later one can't move aside, and the next album going back when putting one back fails.
- `ImportApprovedTracksContainerFixture`: the container builds the importer with its full constructor, and the replacement service with the extra file services and the history repository.

Each test asserts the warnings and errors its path logs, so the fixtures also pass in a Debug build.

The new cases failed before their fixes, and 41 mutations of the change each fail a test.

Full Core suite: 3,277 pass. Merged after `fix/unmapped-duplicates` among the custom build's other 43 branches, it merges cleanly and builds, the image's plugin check passes, and the Core suite there passes 4,125 and fails only the three hardlink tests in `AudioTagServiceFixture`, which this branch doesn't touch.

## Summary by CodeRabbit

- **New Features**
  - Improved album upgrades by temporarily preserving existing track files, lyrics, metadata, and extras during imports.
  - Release switching now occurs only after new files are successfully imported.
  - Previous files are automatically recycled after successful replacements.
  - Added safer handling for interrupted, empty, or partially failed imports, restoring previous files and related information when possible.

- **Bug Fixes**
  - Improved recovery from import failures, file collisions, missing files, and database or filesystem errors.
  - Added collision-safe backup handling and improved cleanup of temporary replacement files.
