# New: Unmonitor albums whose files are deleted from disk

Proposed for the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which would merge this branch into `develop`. Reviewed in [#53](https://github.com/chodeus/Lidarr/pull/53).

- Settings → Media Management → File Management gets **Unmonitor Deleted Albums** back, off by default. It sets `autoUnmonitorPreviouslyDownloadedTracks`, which the API still carries but which has done nothing since tracks stopped being monitored; its checkbox was removed in 6eb70d278.
- With it on, an album is unmonitored once its last file is deleted from disk, so an album another program takes out of the library (such as beets moving it into its own) is not searched for and grabbed again.
- A track file deleted in Lidarr unmonitors its album at once when the album has no other files. Any file still linked to the album counts, including files of its other releases and files a release change has unlinked from their tracks.
- Files a disk scan finds missing unmonitor their album once their whole root folder has been scanned, and only if the album has no files by then. Files in more than one root folder can be covered by separate scans. That scan imports them again if they only moved within the root folder, such as a renamed album or artist folder, and the album stays monitored. Scheduled rescans, the folder watcher and artist refreshes scan whole root folders; adding an artist or an album, or changing an album's release, scans only the artist folder, so what that finds missing waits for the next whole scan.
- Files a scan drops from the database that are still on disk, such as those in folders scans skip like Extras, do not count.
- A scan that matched files to the album without importing them, such as files moved to where they no longer match well enough, leaves it monitored, even a scan of only an artist folder: later scans skip those unmapped files while they are unchanged.
- `DiskScanService` publishes a new `DiskScanCompletedEvent` after its imports and before it records the files it did not import, with the root folders it scanned in full and the albums it matched files to. `ArtistScannedEvent` covers only the artists a scan was given.
- Until a scan decides, RSS sync holds back releases for the album, and keeps holding each album back until the scan has decided it. `DeletedTrackFileSpecification` already held back albums whose files are in the database but no longer on disk; once a scan drops them from the database, a new RSS check, `MissingFromDiskSpecification`, holds back their albums. Removing the root folder the files were in ends the hold, since no scan can decide them after that. A release RSS sync matched to the album before the scan unmonitored it is held back too. Searches are not held back, and nothing is while the setting is off.
- Develop's public types keep their constructors and interfaces, so plugins built against `develop` still load. `DeletedTrackFileSpecification` is unchanged, and the query for whether an album has any file is a new interface, `IAlbumFileQuery`, rather than a member added to `IMediaFileService` or `IMediaFileRepository`.
- Upgrades and manual import overrides replace the files, so they unmonitor nothing. Removing a root folder drops its files from the database and leaves them where they are. It now does so with a deletion reason of its own, `RootFolderRemoved`, instead of `Manual`, so it unmonitors nothing either, even while its storage is offline. Lidarr records no history for these deletions, on `develop` as here, so History is unchanged.
- If the setting is off when a scan completes, the scan unmonitors nothing and forgets what earlier scans found missing. An album deleted in the meantime is skipped.
- The album page shows the album unmonitored as soon as it is. The change is published after the deleted file's own copy of the album, which still has it monitored.

Not changed:

- An album that keeps some of its files stays monitored.
- A root folder that is missing, or holds no audio files, is still never cleaned, so a scan of it finds nothing missing. A scan that stops early, on a missing root folder or an error, decides nothing; the next whole scan does, and RSS sync holds back the album until then.
- What scans have found missing is kept in memory. If Lidarr restarts before a whole scan completes, those albums stay monitored, as on `develop`.
- Files that a scan cannot match to any album at their new path do not keep an album monitored. Nor do files an earlier scan left unmapped before the album's own files went missing, since the scan that decides skips them while they are unchanged.
- A scan drops all the files it found missing from the database at once, then reports them one at a time, so an album's files are gone a moment before RSS sync holds it back: about 0.2 seconds for 25 files in a test run. Holding albums back before the drop would mean the scan's cleanup calling this feature directly.

Second round, from review of the first version (4da6d2d):

- **Root folder removed while offline.** Its files left the database as deleted and were no longer on disk, so their albums were unmonitored. 6025b1e counted a deletion only from a root folder that was still configured, there and not empty; the third round replaces that.
- **Stale album page.** The deleted file carries its own copy of the album, still monitored, and the API broadcast that copy after the change, so the page showed the album monitored until a reload. The album is now unmonitored after the other handlers.
- **Partial scans.** Files found missing were decided by whichever scan completed next, including one of only an artist folder, or one that stopped early. Files moved elsewhere in the root folder unmonitored their album before a scan could find them. They now wait for a scan of their whole root folder.
- **Rejected files.** Files the scan matched to the album without importing them left it unmonitored. They now keep it monitored.
- **Other releases.** Only files of the monitored release, linked to its tracks, counted. Any file of the album now does.
- **Setting turned off.** Turning the setting off before the scan completed still unmonitored the album. The scan now checks it again.
- **RSS.** A release could be grabbed between the scan dropping the files and deciding. It is now held back.
- **Album deleted.** An album deleted while a scan was deciding stopped the rest from being unmonitored. It is now skipped.

Third round, from reviews of 6025b1e, 275999d, 74450406f, 99c20dbc7, 78e2ffbe8 and 61e3d09f7:

- **Root folder removal, again.** Checking that the root folder was still there and not empty failed both ways. With Delete Empty Folders on, deleting an artist's last file can leave the root folder empty, so a real deletion was ignored. Adding the same path back while the removal was still dropping files could unmonitor albums whose files were not there; an end-to-end run hit it once. Root folder removal now drops its files with a reason of its own.
- **Artist folder scans.** A scan of only an artist folder that matched files to the album without importing them was ignored, and the next whole scan skipped those unchanged files and unmonitored the album.
- **Removed root folders.** What scans found missing from a root folder that was then removed was never decided, so RSS sync held back the album until a restart.
- **RSS while the scan decides.** The scan released every hold before unmonitoring any album, so RSS sync could grab one while the scan worked through the others. Each hold now lasts until its album is decided.
- **Last file deleted after a scan found others missing.** The album was unmonitored but its scan entry stayed. Monitoring it again left RSS sync holding it back, and the next whole scan unmonitored it again. Deleting the last file now settles the album.
- **Root folder removed during a scan.** The removal dropped what the scan had recorded so far, and the scan's later reports recorded it again; if the scan then stopped early, RSS sync held back those albums until a restart. Files are now recorded only while their root folder is configured.
- **Root folder removed while the scan decides.** The scan went on deciding the albums the removal had dropped, and the removal can drop an album's remaining files first, which left it unmonitored. Each album is now decided under the lock the removal takes.
- **Album deleted while the scan decides.** `AlbumService.SetMonitored` threw, which left every album after it to the next scan. A failure now leaves only that album.
- **Files in folders scans skip.** Scans drop the files in folders such as Extras from the database although they are still on disk, which unmonitored their album. Only files no longer on disk now count.
- **Root folders scanned one at a time.** An album missing files from two root folders waited for one scan of both, such as the daily rescan. What each scan covers now counts until the rest is covered.
- **Scan failing after its imports.** A scan that failed after recording the files it matched but did not import lost those matches, and later scans skip those files while they are unchanged, so the album was unmonitored. The scan now reports before it records them.
- **RSS sync while the scan decides.** RSS sync loads an album when it matches a release, and checks that it is monitored before it checks the hold. A scan that unmonitored the album and released the hold in between let the release through. The hold check now also rejects an album that is no longer monitored.

From CodeRabbit's reviews of 6025b1e, 99c20dbc7 and 47123ab10:

- **Develop's public types.** The RSS hold had added a constructor parameter to `DeletedTrackFileSpecification`, and `AlbumHasFiles` a member to `IMediaFileService` and `IMediaFileRepository`. They now live in `MissingFromDiskSpecification` and `IAlbumFileQuery`.
- **Debug messages.** The RSS hold said it waited for a scan to unmonitor the album, and a matched album was said to have files the scan did not import; the scan may keep the album monitored, and matched albums include those it imported files for.

Tests: `UnmonitorDeletedAlbumsServiceFixture` runs the real album, track file and root folder services over the test database, and deletes files through `MediaFileService`. It covers the last file, other files, files on another release or not linked to tracks, files still on disk, a deletion that empties the root folder, a root folder removed while its files are offline and after the same path was added back, upgrades and overrides, the setting off when files are deleted and when the scan completes, scans of the whole root folder and of only an artist folder, scans that import the files from a new path or match them without importing, a later scan, a deletion in Lidarr after a scan found some files missing, an album deleted during the scan, the RSS hold for each album until it is unmonitored, and not for searches or with the setting off, its end when the root folder is removed and not when another one is, files reported after their root folder is removed, a root folder removed and an album that cannot be unmonitored while the scan decides, files a scan drops that are still on disk, an album missing from two root folders scanned apart, a release checked while the scan unmonitors its album, and the order against a handler that broadcasts the deleted file's album. `ScanFixture` covers what `DiskScanCompletedEvent` reports, also when a later step of the scan fails. Each new test failed before its fix, and 33 mutations, all building, each fail a test. Full Core suite: 3276 pass, and the 3 tests that fail on Linux on `develop` fail as before. Merged after the custom build's 43 branches, it merges cleanly with no repeated `en.json` keys, and the suite there passes 4130 and fails the same 3.

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

On the same build, releases pushed through `/api/v1/release/push`, which takes the same decisions as RSS sync: with the album's files in place, only the upgrade checks reject one; with its folder moved out of the library and only the artist folder rescanned, the only rejection is "Album files are missing from disk"; after the root folder rescan, "Album is not monitored".
