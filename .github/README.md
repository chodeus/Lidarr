# Fixed: RSS key in logs, housekeeping overlap and repeated-slash backup paths

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`. Reviewed in [#23](https://github.com/chodeus/Lidarr/pull/23).

Three small fixes, one commit each:

- **Logged RSS URLs keep the `r=` key.** The cleanser only hid `r=` after `getnzb`, so a Newznab RSS URL (`/rss?t=-2&dl=1&i=…&r=<key>`) was logged with the key. The rule now covers `rss` too.
- **Housekeeping runs alone.** `HousekeepingCommand` is exclusive, so it waits for running tasks to finish and nothing else starts while it runs.
- **Copying a file whose path has repeated slashes.** `ResolveRealParentPath` took the parent from the raw path but its real casing from `GetActualCasing`, which drops the extra slashes, so the child part was cut at the wrong offset and backups to such a path failed with `DirectoryNotFoundException`. The path is now normalised with `OsPath` (which collapses repeated slashes) before both are taken.

Tests: an RSS URL case in `CleanseLogMessageFixture`, and a copy into a path with tripled separators in `DiskTransferServiceFixture`. Each fails without its fix. Using `GetCleanPath` alone is not enough here: it only trims the trailing slash, and the test still fails with it.

## Summary by CodeRabbit

* **Bug Fixes**
  * File transfers now handle destination paths with repeated directory separators while preserving the copied file’s contents.
  * Secret-like `r` query values in RSS links are removed from log messages, while surrounding link details remain available.
  * Housekeeping tasks now run exclusively, avoiding overlap with other commands.
