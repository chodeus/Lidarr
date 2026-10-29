# Keep artists and albums the metadata source no longer has

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`. Reviewed in [#30](https://github.com/chodeus/Lidarr/pull/30) and [#39](https://github.com/chodeus/Lidarr/pull/39).

- An artist or album the metadata source no longer has is kept only where develop keeps it: an artist with track files, or an album with files or added by hand. For an album, no longer has means not found, or no releases with tracks. The album is marked with the date it went missing (`Albums.MetadataMissingSince`, migration 908); the first date is kept, and the mark clears when the source has the album again. The artist stays marked removed and listed by develop's removed-artist health check.
- Anything else is deleted, as develop does: an album without files, including one that drops out of its artist's list or was marked before its files went. An artist deleted this way leaves its folder on disk, since with no track files anything left in it is unmapped files or extras.
- A health check lists the marked albums. It names the first five, sorted, as "Artist - Album" and counts the rest; the whole list, with MusicBrainz ids, is logged each time it changes. Its link opens the section below.
- New `IAlbumService` and `IAlbumRepository` members have default bodies.

## What to do with a kept album

A kept album is still an ordinary album in Lidarr, with the tracks and files it last had; only the metadata source stopped updating it. Open it from its artist's page, or at `/album/<mbid>` (the log line "Albums kept after the metadata source dropped them" has the ids).

1. Look the release group up on MusicBrainz (`https://musicbrainz.org/release-group/<mbid>`). If it is still there, its releases lack something your metadata profile requires, usually the Official status. Fixing that on MusicBrainz brings the album back on a later refresh and clears the warning.
2. If it was deleted, see whether its songs are on another album you have. If they are, delete the album with **Delete files** ticked; the copies go to the recycle bin. If they are not, delete it with **Delete files** unticked: its files move to Library → Unmapped Files, where you can import them onto another album or leave them.
3. **Delete** is at the top of the album's page. Untick **Add list exclusion** if the album may come back on MusicBrainz.

Tests: refresh cases for an album with files or added by hand (kept and marked when not found, with no valid releases, or unlisted and gone) and an album without files (deleted when not found, with no valid releases, unlisted without asking the source, or marked before), keeping the first date and clearing it, publishing both changes, develop's constructor; an artist without files deleted with its folder left; the repository query; the health check (artist names, the first five and the count, the link, logging the list once per change); the default bodies. Each was checked to fail with its rule broken. develop's tests for an album without valid releases and an artist without files are back as develop has them.

## Summary by CodeRabbit

* **New Features**
  * Albums retained when missing from metadata are marked with the date they went missing; the date clears when metadata returns.
  * Health checks warn when albums are missing from the metadata source and identify each affected album.
* **Bug Fixes**
  * Albums with files, added by hand, or synced from the source before are kept and marked missing when the source no longer has them or has no usable releases.
  * Artists missing from metadata are marked as deleted without being physically removed.
