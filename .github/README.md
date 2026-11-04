# Fixed: Import a download against the album it was grabbed for

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`. Reviewed in [#76](https://github.com/chodeus/Lidarr/pull/76).

An artist with two albums of one name (a single and its album) had a download parsed to the wrong one; the import then refused the files as the other album's tracks.

- Completed downloads, and scans of a known download, import against the one album the grab named, as a single release
- The tracked download is pointed at that album first, so verification, the already-imported check, the `DownloadImported` history row and the queue all name it
- No grab history, a grab of several albums (even after one is deleted), or another artist's album keeps develop's artist-wide import
- New interface members have default bodies, so develop's surface is unchanged

Summary by CodeRabbit:

- Completed downloads can now be matched and imported against the album associated with the original grab, keeping album tracking and import history aligned.
- When the grabbed album cannot be identified confidently, imports continue using the usual identification process.
- Scans of known downloads use the album-aware import path when an album is available, for both folders and individual files.

Tests: `GrabbedAlbumImportFixture` runs on the real history, album, artist and parsing services. A grab naming one album imports against it, also when grabbed twice, and points the tracked download at it with its releases. The `DownloadImported` row names it, and a second pass that finds every file already imported into it ends Imported. Several grabbed albums (also after one is deleted), another artist's album or no grab history keep develop's import. `DownloadedTracksImportGrabbedAlbumFixture` judges a folder as one release of the album and a file against it, and leaves `ProcessPath` as develop has it. `DownloadedAlbumsScanGrabbedAlbumFixture` does the same for a scan of a known download. Each of these mutations fails a test: not pointing the tracked download at the album, dropping the several-albums guard, and a grabbed file skipping the decision maker.
