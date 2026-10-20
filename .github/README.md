# Fixed: Stop re-grabbing a torrent qBittorrent already has

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`. Reviewed in [#22](https://github.com/chodeus/Lidarr/pull/22).

qBittorrent answers `409 Conflict` to `torrents/add` when it already has the torrent. The proxy wraps that in a `DownloadClientException` ("Failed to connect to qBittorrent, check your settings."), so an RSS or search grab logs a connection failure with a stack trace for a duplicate, and a manual grab returns HTTP 500 because `ReleaseController` only catches `ReleaseDownloadException`.

Both add paths (magnet and torrent file) now turn a 409 into `DownloadClientRejectedReleaseException`, which `DownloadService` already logs as a possible duplicate without recording an indexer failure. Every other proxy error is unchanged.

Tests: a 409 on a magnet link and on a torrent file each throw `DownloadClientRejectedReleaseException`, and a 500 still throws `DownloadClientException`. Without the change the two 409 tests fail.

Second round: stop re-grabbing it.

The rejection alone didn't end the loop. `ProcessDownloadDecisions` logs "Couldn't add release" and skips the release, so every RSS sync tried it again until the listing left the feed, and a "Fails." reply, which older qBittorrent versions send instead of 409, was still a client error. It happens when the copy qBittorrent holds sits in a category Lidarr's queue doesn't watch: a torrent Lidarr imported and moved to its post-import category, or one a cross-seed tool added.

- A 409, or a "Fails." reply on API v2 (qBittorrent 4.1 and newer), asks qBittorrent whether it has that hash (`HasTorrent`, through the proxy's `IsTorrentLoaded`). If it does, the add throws `ReleaseAlreadyInDownloadClientException`, a kind of `DownloadClientRejectedReleaseException`, so everything that handled the rejection still does. If the hash isn't there, or the check fails, a 409 stays a plain rejection and "Fails." a client error. The API v2 proxy now throws `QBittorrentAddFailedException`, a kind of `DownloadClientException`, for "Fails.". API v1 keeps it a plain failure, and `HasTorrent` answers no there, because its torrent check can't tell a missing torrent apart.
- `DownloadService` remembers that release for 12 hours (`DuplicateReleaseCache`, in memory; 12 hours is `HistorySpecification`'s window for a recent grab), by the hash qBittorrent confirmed and by the listing's GUID on that indexer and protocol. The exception carries that hash and the client that refused it, so the cache doesn't depend on how `DownloadService` picked the client.
- `AlreadyInDownloadClientSpecification` rejects a remembered release with "<client> already has this release". A listing with a hash matches by hash, whichever indexer lists it; one without a hash matches only the same listing. It first asks the client that refused the torrent whether it still has it: once you delete it from qBittorrent the next sync can grab it again, and a client that can't be asked doesn't block the grab. A grab refused while that check runs keeps its entry.
- Nothing is written to the blocklist or the database. After 12 hours, or a restart, Lidarr tries once more, and if qBittorrent still has the torrent the rejection starts again.
- Develop's `DownloadService` constructor is kept for plugins; built that way, nothing is remembered.

Tests: `QBittorrentAlreadyHasTorrentFixture` covers a 409 and a "Fails." reply for a torrent file and a magnet link the client already has, one it doesn't, a check that fails, and API v1. `QBittorrentProxyV2Fixture` and `QBittorrentProxyV1Fixture` run the real proxies over a mocked HTTP client. `DuplicateReleaseFixture` runs the real `DownloadService`, cache and specification with only the download client mocked: the same listing on a later sync, the same hash from another indexer, the same title elsewhere, other listings, GUIDs and protocols, pushed listings, the hash the client confirmed, the recheck, a client that can't be checked, a grab refused during a recheck, and a 409 through the real `QBittorrent` client. `DownloadServiceContainerFixture` checks the container builds the service with the cache. 27 mutations each fail a test; the lock around the cache's writes has no deterministic test. The download and decision engine tests pass in Release and Debug, and the full Core suite passes 3263. Merged in order after the custom build's other branches, it merges cleanly, builds, passes the plugin check, and the download and decision engine tests pass there.

## Summary by CodeRabbit

- **Bug Fixes**
  - qBittorrent conflicts when submitting a magnet link or torrent file are now reported as rejected releases.
  - Releases already present in qBittorrent are identified as duplicates and won’t be downloaded again.
  - Duplicate detection is remembered temporarily and rechecked, allowing downloads after the existing torrent is removed.
  - Other download errors, including server errors and unconfirmed conflicts, continue to be reported as download errors or rejections.
