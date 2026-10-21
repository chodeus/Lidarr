# Fixed: A queued rescan no longer fails when one of its artists was deleted

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`. Reviewed in [#58](https://github.com/chodeus/Lidarr/pull/58).

A rescan command names the artists it is for by id. `DiskScanService.Scan` read them back with `GetArtists`, which throws when an id is no longer stored, and also when one is repeated, and it did so after walking its folders. Commands wait in a queue, and a delete through the API or the UI doesn't, so an artist deleted before or during the scan failed it for every artist on it. By then the scan had already cleaned the database rows of files missing from its folders, so nothing was imported or matched in that run.

- The scan reads its artists once, before any folder is cleaned, leaving out ids that are no longer stored, using the stored artist paths, and repeats. A scan with no artist ids is unchanged.
- The artists are only used to report skipped artists and to check for missing artist folders. One deleted while the scan runs is still on that list, so at most it is reported or its folder is checked; the scan doesn't fail.

Tests: `ScanDeletedArtistFixture` runs the scan against the real `ArtistService` and repository over the test database; the disk, import decisions and media files are mocked. A scan naming a deleted artist still checks the stored artist's folder and makes its import decisions, as does one whose artist is deleted while it reads the folders; a missing root folder reports only the stored artist as skipped, and an artist named twice is checked once. All four fail on `develop`, the mid-scan one also with the ids filtered only at the start; leaving out either the filter or the de-duplication fails a test. The two `UpgradeMediaFileServiceFixture` tests that fail in a Debug build on a stray warning fail on `develop` too.
