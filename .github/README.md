# Fixed: Removing an unmatched queue item without blocklisting

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`. Reviewed in [#13](https://github.com/chodeus/Lidarr/pull/13).

A queue item that never mapped to an artist (for example "found multiple artists") has no `RemoteAlbum`. Removing it with `removeFromClient=false&blocklist=false` reached `IgnoredDownloadService.IgnoreDownload`, which read `trackedDownload.RemoteAlbum.Artist` and threw, so the API returned a 500. `IgnoreDownload` now ignores such a download by its download id alone: the download history records it as ignored with no artist or album, so it stays ignored when the queue is refreshed, as a matched download does. Activity's history lists ignores by album, so an unmatched one doesn't show there. Reading that ignore back when the download still doesn't match an artist needs `fix/tracked-download-remap`, later in this build, which reads the download history even when mapping fails.

`IgnoredDownloadServiceFixture` covers a matched download (ignored with its artist and album), a download with no `RemoteAlbum`, and one with no albums (both ignored by download id). `IgnoredUnmatchedDownloadHistoryFixture` runs the real ignore and history services over the test database: the unmatched download's latest download history is the ignore, and no album history is written. Refusing unmatched downloads again, or reading the artist's id without a null check, fails a test.

## Summary by CodeRabbit

* **Bug Fixes**
  * Downloads missing artist or album information are now safely marked as ignored by download ID.
  * Ignored-download notifications are published even when artist or album details are unavailable.
  * Matched downloads continue to record their associated artist and album information.

* **Documentation**
  * Added guidance on handling unmatched downloads and their history records.
