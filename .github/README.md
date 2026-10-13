# Fixed: Treat qBittorrent's conflict response as a rejected release

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`. Reviewed in [#22](https://github.com/chodeus/Lidarr/pull/22).

qBittorrent answers `409 Conflict` to `torrents/add` when it already has the torrent. The proxy wraps that in a `DownloadClientException` ("Failed to connect to qBittorrent, check your settings."), so an RSS or search grab logs a connection failure with a stack trace for a duplicate, and a manual grab returns HTTP 500 because `ReleaseController` only catches `ReleaseDownloadException`.

Both add paths (magnet and torrent file) now turn a 409 into `DownloadClientRejectedReleaseException`, which `DownloadService` already logs as a possible duplicate without recording an indexer failure. Every other proxy error is unchanged.

Tests: a 409 on a magnet link and on a torrent file each throw `DownloadClientRejectedReleaseException`, and a 500 still throws `DownloadClientException`. Without the change the two 409 tests fail.

## Summary by CodeRabbit

- **Bug Fixes**
  - qBittorrent conflicts when submitting a magnet link or torrent file are now reported as rejected releases.
  - Other download errors, including server errors, continue to be reported as download errors.
