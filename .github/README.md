# Fixed: Unmapped duplicates from rescans and upgrades

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`. Reviewed in [#26](https://github.com/chodeus/Lidarr/pull/26).

Two ways unmapped files fed themselves on a live library:

**Rescans re-imported linked files every day.** When a refresh's `Matched` rescan picks up an unmapped file, identification pulls the album's existing files in with it (`IncludeExisting`). `ImportApprovedTracks` then re-imported the linked file: deleted its row, inserted a new one with today's `DateAdded`, and rewrote its tags. With a duplicate copy in a single's folder this happened on every metadata-updating refresh, and which copy won could flip as track matching changed. An existing file that is unchanged on disk and already linked to exactly the tracks it was identified for is now skipped. The skipped result still counts as "already imported", so a second file can't take its tracks, and an album with only skipped files doesn't publish `AlbumImportedEvent`.

The size and modified-time comparison was written out twice in `MediaFileService.FilterUnchangedFiles`; it is now `TrackFile.IsUnchanged`, used by both filter modes and the new check.

**Upgrades left orphaned copies behind.** A download import removes the album's previous files through `RemoveExistingTrackFiles`, which finds them by `AlbumId`. A copy that had lost its album (`AlbumId` 0) in the same folder survived, and the new file landed beside it as a duplicate. After a download import, unmapped `AlbumId` 0 files in the folder of the imported files are recycled when their tags name an imported track: the same recording id, or (when either side has none) the same disc, track number and normalised title. A copy whose quality beats every imported match stays, with order inside a quality group counted, so FLAC 24bit beats FLAC in the default Lossless group. Files in subfolders, other titles and other recordings stay too. Any failure here is logged and never fails the import.

Tests: 13 in `ImportApprovedTracksFixture`. Nine mutations, one per rule, each fail at least one of them. All 23 custom-build branches merge with this. The merged tree passes the full Core suite (3559 tests) except three hardlink tests in `AudioTagServiceFixture`, which this branch doesn't touch.

## Summary by CodeRabbit

* **Bug Fixes**
  * Avoided re-importing unchanged files already linked to the same tracks.
  * Improved replacement of orphaned track files: lower- or equal-quality matches may be recycled, while better-quality copies and files linked elsewhere are retained.
  * Improved detection of unchanged files when modification times differ by up to one second.
