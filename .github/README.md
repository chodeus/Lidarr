# Fixed: A queued rescan no longer fails when one of its artists was deleted

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`. Reviewed in [#PR_NUMBER](https://github.com/chodeus/Lidarr/pull/PR_NUMBER).

A rescan command names the artists it is for by id. `DiskScanService.Scan` reads them back with `GetArtists`, which throws when an id is no longer stored, and also when one is repeated. Commands wait in a queue, so an artist deleted through the API or the UI between the queueing and the scan failed the scan for every artist on it. By then the scan had already cleaned the database rows of files missing from its folders, so nothing was imported or matched in that run.

- The scan leaves out ids that are no longer stored, using the stored artist paths, and repeats, before its first `GetArtists`. A scan with no artist ids is unchanged.
- The ids are only used to report skipped artists and to check for missing artist folders, so leaving out a deleted one changes nothing else.

Tests: `ScanDeletedArtistFixture` runs the scan against the real `ArtistService` and repository over the test database; the disk, import decisions and media files are mocked. A scan naming a deleted artist still checks the stored artist's folder and makes its import decisions, a missing root folder reports only the stored artist as skipped, and an artist named twice is checked once. All three fail on `develop`; leaving out either the filter or the de-duplication fails a test. The two `UpgradeMediaFileServiceFixture` tests that fail in a Debug build on a stray warning fail on `develop` too.
