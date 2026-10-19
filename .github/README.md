# New: Unmonitor albums whose files are deleted from disk

Proposed for the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which would merge this branch into `develop`. Reviewed in [#53](https://github.com/chodeus/Lidarr/pull/53).

- Settings → Media Management → File Management gets **Unmonitor Deleted Albums** back, off by default. It sets `autoUnmonitorPreviouslyDownloadedTracks`, which the API still carries but which has done nothing since tracks stopped being monitored; its checkbox was removed in 6eb70d278.
- With it on, an album is unmonitored once its last file is deleted from disk, so an album another program takes out of the library (such as beets moving it into its own) is not searched for and grabbed again.
- A track file deleted in Lidarr unmonitors its album at once when the album has no other files. Any file still linked to the album counts, including files of its other releases and files a release change has unlinked from their tracks.
- Files a disk scan finds missing unmonitor their album once a scan of their whole root folder completes, and only if the album has no files by then. That scan imports them again if they only moved within the root folder, such as a renamed album or artist folder, and the album stays monitored. Scheduled rescans, the folder watcher and artist refreshes scan whole root folders; adding an artist or an album, or changing an album's release, scans only the artist folder, so what that finds missing waits for the next whole scan.
- A scan that matched files to the album without importing them, such as files moved to where they no longer match well enough, leaves it monitored.
- `DiskScanService` publishes a new `DiskScanCompletedEvent` after its imports, with the root folders it scanned in full and the albums it matched files to. `ArtistScannedEvent` covers only the artists a scan was given.
- Until that scan completes, RSS sync holds back releases for the album. `DeletedTrackFileSpecification` already held back albums whose files are in the database but no longer on disk; once a scan drops them from the database, it now holds back their albums too.
- Upgrades and manual import overrides replace the files, so they unmonitor nothing. Removing a root folder drops its files from the database and leaves them where they are, so it unmonitors nothing either, even while its storage is offline.
- If the setting is off when a scan completes, the scan unmonitors nothing and forgets what earlier scans found missing. An album deleted in the meantime is skipped.
- The album page shows the album unmonitored as soon as it is. The change is published after the deleted file's own copy of the album, which still has it monitored.

Not changed:

- An album that keeps some of its files stays monitored.
- A root folder that is missing, or holds no audio files, is still never cleaned, so a scan of it finds nothing missing. A scan that stops early, on a missing root folder or an error, decides nothing; the next whole scan does.
- What scans have found missing is kept in memory. If Lidarr restarts before a whole scan completes, those albums stay monitored, as on `develop`.
- Files that a scan cannot match to any album at their new path do not keep an album monitored.
- Removing a root folder drops its files from the database in the background. Adding the same path back before that finishes can unmonitor an album whose files are not there, if the root folder itself is reachable and holds other music.

Second round, from review of the first version (4da6d2d):

- **Root folder removed while offline.** Its files left the database as deleted and were no longer on disk, so their albums were unmonitored. A deletion now counts only for a file gone from a root folder that is still configured, exists and is not empty.
- **Stale album page.** The deleted file carries its own copy of the album, still monitored, and the API broadcast that copy after the change, so the page showed the album monitored until a reload. The album is now unmonitored after the other handlers.
- **Partial scans.** Files found missing were decided by whichever scan completed next, including one of only an artist folder, or one that stopped early. Files moved elsewhere in the root folder unmonitored their album before a scan could find them. They now wait for a scan of their whole root folder.
- **Rejected files.** Files the scan matched to the album without importing them left it unmonitored. They now keep it monitored.
- **Other releases.** Only files of the monitored release, linked to its tracks, counted. Any file of the album now does.
- **Setting turned off.** Turning the setting off before the scan completed still unmonitored the album. The scan now checks it again.
- **RSS.** A release could be grabbed between the scan dropping the files and deciding. It is now held back.
- **Album deleted.** An album deleted while a scan was deciding stopped the rest from being unmonitored. It is now skipped.

Tests: `UnmonitorDeletedAlbumsServiceFixture` runs the real album, track file and root folder services over the test database, and deletes files through `MediaFileService.DeleteMany`. It covers the last file, other files, files on another release or not linked to tracks, files still on disk, a removed root folder and one that is missing or empty, upgrades and overrides, the setting off when files are deleted and when the scan completes, scans of the whole root folder and of only an artist folder, a scan that imports the files from a new path or matches them without importing, a later scan, an album deleted during the scan, the RSS hold, and the order against a handler that broadcasts the deleted file's album. `ScanFixture` covers what `DiskScanCompletedEvent` reports. Each new test failed before its fix, and 19 mutations, all building, each fail a test. Full Core suite: 3262 pass, and the 3 tests that fail on Linux on `develop` fail as before. Merged after the custom build's 43 branches, it merges cleanly with no repeated `en.json` keys, and the suite there passes 4116 and fails the same 3.

On a build, with the folder watcher off and scans started through the API, an album of 25 files in a root folder that also holds another artist, and beside another folder of the same artist:

| | `develop` | 4da6d2d | this branch |
|---|---|---|---|
| Album folder renamed, root folder rescanned | monitored, 25 files | monitored, 25 files | monitored, 25 files |
| Album folder moved out of the library, root folder rescanned | monitored | unmonitored | unmonitored |
| Moved out, only the artist folder rescanned | monitored | unmonitored | monitored |
| Then the root folder rescanned | monitored | unmonitored | unmonitored |
| Files deleted through the API | monitored | unmonitored, page shows monitored until a reload | unmonitored, page updates at once |
| Root folder removed while the album folder was moved out | monitored | unmonitored | monitored |
| Root folder removed with the files on disk | monitored | monitored | monitored |
