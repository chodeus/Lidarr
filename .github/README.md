# Fixed: Release group from the download shown in Manual Import

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`. Reviewed in [#93](https://github.com/chodeus/Lidarr/pull/93).

Manual Import's preview never filled the release group. `MapItem` copied the artist, album, tracks, quality and indexer flags from each import decision, but not the release group the decision had parsed. The column stayed empty, and importing from the dialog saved the files with no release group even when the download title named one; a re-processed row got it back, a fresh one never did. A single-file download didn't pass its tracked download to the import decision at all, so only the file's tags could name a group or a quality.

- `MapItem` copies the decision's release group (download title, then folder name, then tags).
- A single-file download passes its tracked download to the import decision as a folder does, so its release group, quality and already-imported check read the download title too.
- The tracked download is looked up once in `GetMediaFiles` and passed to `ProcessFolder`, which looked it up again and read it without a null check.

Summary by CodeRabbit:

- Manual imports now retain release-group information for both folder-based and single-file downloads.
- Folder imports can use the artist associated with the tracked download when no artist is otherwise identified.

Tests: `ManualImportServiceFixture` returns a release group from the import decision only when it receives the tracked download's item, and asserts the group on the preview row for a download folder and for a single-file download. Both fail on `develop`; passing no item for single files, passing none for folders, or dropping the copy in `MapItem` each fails a test.
