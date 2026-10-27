# Keep artists and albums the metadata source no longer has

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`. Reviewed in [#30](https://github.com/chodeus/Lidarr/pull/30) and [#39](https://github.com/chodeus/Lidarr/pull/39).

- An artist the metadata source reports as not found is kept, whether or not it has files. It stays marked removed and listed by the existing removed-artist health check; its folder is no longer deleted.
- An album the source no longer has (not found, or no releases with tracks) is kept and marked with the date it went missing (`Albums.MetadataMissingSince`, migration 908) when there is something to keep: it has files, was added by hand, or was synced from the source at least once. An album the source listed but never had a usable copy of (a video-only release, say) is deleted, as develop does. The first date is kept; the mark clears when the source has the album again.
- An album without files and not added by hand that drops out of its artist's list is removed as before if it was never synced. Otherwise it is checked against the source first. If the source moved it to another artist, or the artist's metadata profile now leaves it out, it is removed as before. If the source still has it under the same artist and the profile allows it, the artist's list only missed it, so it is kept and refreshed as usual. If the source no longer has it, it is kept and marked. If that check fails (timeout, 5xx) the album is left alone until the next refresh, and so are the rest of that artist's unlisted albums in that refresh. An album already marked is checked against the source like a synced one, so a refresh removes it only if the source moved it or the profile now leaves it out.
- A new health check lists the marked albums.
- New `IAlbumService` and `IAlbumRepository` members have default bodies. `RefreshAlbumService` keeps develop's constructor; the container uses a new one that adds the metadata profile service.

Tests: refresh cases for not found, no valid releases, unlisted and moved to another artist, excluded by the profile or still listed under the same artist, unlisted and gone, a failed source check and the unlisted albums after it, an album already marked, keeping the first date and clearing it, never-synced albums (deleted, without a source check when unlisted, unless they have files or were added by hand); the repository query; the health check messages; the default bodies; develop's constructor and the container choosing the new one. Each was checked to fail with its rule broken. Four develop tests that expected deletion now expect the album or artist to be kept.

## Summary by CodeRabbit

* **New Features**
  * Albums retained when missing from metadata are marked with the date they went missing; the date clears when metadata returns.
  * Health checks warn when albums are missing from the metadata source and identify each affected album.
* **Bug Fixes**
  * Albums with files, added by hand, or synced from the source before are kept and marked missing when the source no longer has them or has no usable releases.
  * Artists missing from metadata are marked as deleted without being physically removed.
