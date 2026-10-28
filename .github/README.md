# Fixed: Link extra files imported with a track to its track file

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`. It is stacked on [`fix/replace-import-rollback`](https://github.com/chodeus/Lidarr/tree/fix/replace-import-rollback), which reworks the same import loop, and merges after it.

With Import Extra Files on, a download's lyrics (`.lrc`, `.txt`) and other extra files are imported beside each track. Develop imports them inside the track loop of `ImportApprovedTracks`, before `AddMany` saves the new track files, so each track file still has id 0 and every extra file row is stored with `TrackFileId` 0. Renaming the tracks then leaves those extra files behind under the old names, and deleting or upgrading the track leaves them in the album folder. The artist scan keeps such a row as long as its file is on disk.

- Extra files are imported after `AddMany` (and after each album's backed-up files are settled), so each row links to its track file. An extra file that fails to import logs a warning; the track still counts as imported. Before, the failure marked the already-moved track "Failed to import track".
- Two housekeeping tasks, ported from Sonarr's `CleanupOrphanedExtraFiles` and `CleanupOrphanedSubtitleFiles`, clean the `ExtraFiles` and `LyricFiles` tables as `CleanupOrphanedMetadataFiles` already cleans `MetadataFiles`: rows whose artist or track file is gone, and rows with `TrackFileId` 0. The files stay on disk, and the next artist scan imports them again, linked to their track files.

Tests: extra files imported with a track over the real services and test database (both rows linked to the new track file; failed before the change with `TrackFileId` 0); an extra file import that throws (the track still imports, with a warning); each housekeeping rule for both tables, including a row that must stay. Each was broken on purpose and caught by a test.
