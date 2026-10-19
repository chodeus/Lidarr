# New: Unmonitor albums whose files are deleted from disk

Proposed for the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which would merge this branch into `develop`. Reviewed in [#53](https://github.com/chodeus/Lidarr/pull/53).

- Settings → Media Management → File Management gets **Unmonitor Deleted Albums** back, off by default. It sets `autoUnmonitorPreviouslyDownloadedTracks`, which the API still carries but which has done nothing since tracks stopped being monitored; its checkbox was removed in 6eb70d278.
- With it on, an album is unmonitored once its last file is deleted from disk, so an album another program takes out of the library (such as beets moving it into its own) is not searched for and grabbed again.
- A track file deleted in Lidarr unmonitors its album at once when the album has no other files.
- Files a disk scan finds missing unmonitor their album when the scan completes, and only if the album still has no files then: a scan that imports them again from a new path, such as a renamed folder, leaves it monitored. `DiskScanService` publishes a new `DiskScanCompletedEvent` after its imports, for root folder scans as well as artist scans, since `ArtistScannedEvent` covers only the artists a scan was given.
- Upgrades and manual import overrides replace the files, so they unmonitor nothing. Removing a root folder drops its files from the database but leaves them on disk, so it unmonitors nothing either. An album deleted while a scan runs is dropped from that scan's list.
- The RSS check that already read this setting (`DeletedTrackFileSpecification`) is unchanged. Its log line, "will be unmonitored on next diskscan", is now true.

Not changed:

- An album that keeps some of its files stays monitored.
- A scan that finds an artist's folder missing drops that artist's files, as before; with the setting on it now also unmonitors their albums. A root folder that is missing or empty is still never cleaned.
- The list of albums a scan found missing is kept in memory. If Lidarr restarts before the scan completes, those albums stay monitored; a scan that stops early leaves them for the next one to complete.

Tests: against the real album and track file repositories, an album is unmonitored when its last file is deleted, and stays monitored while it has other files, when its files are still on disk, when an upgrade or a manual import override replaces them, and with the setting off. An album whose files a scan finds missing stays monitored until the scan completes, is unmonitored then, stays monitored when the scan imports its files from a new path, and is not unmonitored again by a later scan once monitored again; an album deleted during the scan does not stop the others. A scan reports itself completed after its imports. Each rule was broken on purpose and caught by a test. `Lidarr.Core.Test` has the same 3 failures on Linux as `develop` (two Windows-path deletion fixtures, and an import list test that passes on its own). The branch merges cleanly after every branch in the custom build, and its tests pass there.

Run on a build with a scratch library: Rescan Folders imported The Postal Service's *Give Up* (25 silent FLACs tagged with its MusicBrainz ids). Renaming its folder inside the library and rescanning left it monitored with all 25 files; moving the folder out of the library and rescanning unmonitored it; deleting its files through the API unmonitored it; removing the root folder left it monitored. `develop` left it monitored after every step. The checkbox shows the stored setting and saves a change.
