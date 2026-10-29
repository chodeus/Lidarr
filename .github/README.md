# Expect the warnings three tests log, and capture the first test's warnings

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`. It changes tests only.

Lidarr's tests fail on an unexpected warning only in a Debug build, so three of develop's tests fail in Debug and pass in Release:

- `ImportListServiceFixture.should_remove_missing_import_lists_on_startup` logs `Removing <list>`, and `UpgradeMediaFileServiceFixture.should_delete_existing_file_fromdb_if_file_doesnt_exist` and `should_not_try_to_recyclebin_existing_file_if_file_doesnt_exist` log `Existing track file missing from disk`. Each now ends with `ExceptionVerification.ExpectedWarns(1)`, as Lidarr's own `IndexerServiceFixture.should_remove_missing_indexers_on_startup` and Sonarr's versions of all three do.
- The first test in a test run captured no warnings: `NzbDroneLogger`'s static constructor replaces the logging configuration the first time a logger is made, after `LoggingTest` has added its capture target. `LoggingTest` now runs that constructor first. The change is the one `feat/release-profile-tags-indexers` makes, byte for byte, so the two merge cleanly.

Checked: each of the three tests run on its own passes, and fails with "1 Warn(s) were expected but 0 were logged" without the `LoggingTest` change; with each warning lowered to Debug in `UpgradeMediaFileService` and `ProviderFactory`, its tests fail.

Full Core suite: 3,247 pass in both Debug and Release, with no failures. Merged after the custom build's 54 other branches, it merges cleanly and builds, `en.json` has no repeated keys, and the Core suite there passes 4,691 in both Debug and Release.
