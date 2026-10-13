# Fixed: Removing an unmatched queue item without blocklisting

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`. Reviewed in [#13](https://github.com/chodeus/Lidarr/pull/13).

A queue item that never mapped to an artist (for example "found multiple artists") has no `RemoteAlbum`. Removing it with `removeFromClient=false&blocklist=false` reached `IgnoredDownloadService.IgnoreDownload`, which read `trackedDownload.RemoteAlbum.Artist` and threw, so the API returned a 500. `IgnoreDownload` now treats a missing `RemoteAlbum` or album list like an unknown artist: it logs the existing warning and returns false, and the controller ignores nothing.

`IgnoredDownloadServiceFixture` covers a matched download (still ignored, event published), a download with no `RemoteAlbum`, and one with no albums. Both negative tests fail on the unchanged code.

## Summary by CodeRabbit

* **Bug Fixes**
  * Downloads with missing artist or album information, including downloads without an album list, are safely left unignored and do not generate an ignored-download notification. Downloads with a mapped artist and album continue to be marked as ignored and generate the corresponding notification. Incomplete download details also trigger a warning.
