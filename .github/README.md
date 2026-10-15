# Keep artists and albums the metadata source no longer has

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`. Reviewed in [#30](https://github.com/chodeus/Lidarr/pull/30).

- An artist the metadata source reports as not found is kept, whether or not it has files. It stays marked removed and listed by the existing removed-artist health check; its folder is no longer deleted.
- An album the source no longer has (not found, or no releases with tracks) is kept and marked with the date it went missing (`Albums.MetadataMissingSince`, migration 908) when there is something to keep: it has files, was added by hand, or was synced from the source at least once. An album the source listed but never had a usable copy of (a video-only release, say) is deleted, as develop does. The first date is kept; the mark clears when the source has the album again.
- An album without files that drops out of its artist's list is removed as before if it was never synced. Otherwise it is checked against the source first: if the source still has it (excluded by the metadata profile, or moved to another artist) it is removed as before; if not, it is kept and marked. If that check fails (timeout, 5xx) the album is left alone until the next refresh.
- A new health check lists the marked albums.
- New `IAlbumService` and `IAlbumRepository` members have default bodies.

Tests: refresh cases for not found, no valid releases, unlisted and still in the source, unlisted and gone, a failed source check, keeping the first date and clearing it, never-synced albums (deleted, without a source check when unlisted, unless they have files or were added by hand); the repository query; the health check messages; the default bodies. Each was checked to fail with its rule broken. Four develop tests that expected deletion now expect the album or artist to be kept.

## Summary by CodeRabbit

* **New Features**
  * Albums no longer found in the metadata source are retained and marked with the date they went missing. That date is cleared if the album returns.
  * Health checks warn when albums are missing from the source, identifying each album whether one or several are affected.
* **Bug Fixes**
  * Albums without valid releases from the source are kept and marked as missing rather than deleted.
  * Artists missing from metadata are marked deleted without being physically removed.
