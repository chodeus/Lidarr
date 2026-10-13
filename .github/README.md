# New: Retry failed imports after a refresh updates the album

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`. Reviewed in [#7](https://github.com/chodeus/Lidarr/pull/7).

A download that fails import against old album metadata stays in `ImportFailed` until someone retries it by hand. When a refresh changes an album (`AlbumUpdatedEvent` fires only then), this handler reloads the albums of the artist's failed downloads and moves them to `ImportBlocked`, the state the next monitoring pass retries. A retry that fails again returns to `ImportFailed`, so each refresh retries a download at most once.

- `ImportFailedRetryService`: the event handler.
- `ImportFailedRetryServiceFixture`: retry, album swap, other artist, other states, unmapped download.

## Summary by CodeRabbit

* **New Features**
  * Failed imports are now retried after an album is updated, when the download matches the updated artist.
  * Retried downloads use refreshed album information, helping them proceed with the latest metadata.
