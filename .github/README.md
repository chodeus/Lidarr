# New: Retry failed imports after a refresh updates the album

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`. Reviewed in [#7](https://github.com/chodeus/Lidarr/pull/7).

A download that fails import against old album metadata stays in `ImportFailed` until someone retries it by hand. When a refresh changes an album (`AlbumUpdatedEvent` fires only then), the handler notes the album's artist. When the refresh command finishes (`RefreshArtist` or `RefreshAlbum`), it reloads the albums of that artist's failed downloads and moves them to `ImportBlocked`, the state the next monitoring pass retries. A refresh retries a download once, however many of the artist's albums it changed. A retry whose tracks are rejected again returns to `ImportFailed`. One that finds nothing to import waits in `ImportPending`, as a first attempt would.

Skipped:
- A download whose albums no longer map (an album was deleted), since there is nothing to import against. Develop counts a download with no albums as imported, and its client would then remove it.
- A download that a manual import finished while its albums were loading.

A download whose albums can't be loaded is logged and skipped, and the artist's other downloads are still retried.

- `ImportFailedRetryService`: the handlers.
- `ImportFailedRetryServiceFixture`: retry, album swap, timing, other commands, other artist, other states, unmapped downloads.
- `ImportFailedRetryPipelineFixture`: the real tracked-download, parsing, monitoring and import services over full monitoring passes.
- `ImportFailedRetryDbFixture`: the album reload over the test database.

## Summary by CodeRabbit

* **New Features**
  * Failed imports can now be retried after a matching artist or album refresh completes, using the refreshed album information.
  * Each failed download is retried at most once per refresh. Downloads that no longer map to an album or artist are skipped.
  * If a retry still encounters rejected tracks, the download remains marked as a failed import; if there are no tracks to import, it remains pending.
