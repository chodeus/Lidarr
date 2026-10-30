# Keep artists and albums the metadata source no longer has

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`. Reviewed in [#30](https://github.com/chodeus/Lidarr/pull/30), [#39](https://github.com/chodeus/Lidarr/pull/39), [#74](https://github.com/chodeus/Lidarr/pull/74) and [#75](https://github.com/chodeus/Lidarr/pull/75).

- An artist or album the metadata source no longer has is kept only where develop keeps it: an artist with track files, or an album with files or added by hand. For an album, no longer has means not found, or no releases with tracks. The album is marked with the date it went missing (`Albums.MetadataMissingSince`, migration 908); the first date is kept, and the mark clears when the source has the album again. The artist stays marked removed and listed by develop's removed-artist health check.
- Anything else is deleted, as develop does: an album without files, including one that drops out of its artist's list or was marked before its files went. An artist deleted this way leaves its folder on disk, since with no track files anything left in it is unmapped files or extras.
- A health check lists every marked album without files, sorted, as "Artist - Album": it has no release Lidarr can use, so it can't be downloaded. A marked album with files needs nothing done and is left out; each refresh of it logs that it is kept. The check's link opens the section below.
- Deleting an album with its files no longer fails on paths that aren't on disk. develop's cleanup listed the artist and album folders without checking they were there, and a track file already gone stopped the move to the recycle bin for every file after it; these are now skipped.
- New `IAlbumService` and `IAlbumRepository` members have default bodies.

## What to do with an album in the warning

Open the album in Lidarr (search for its name), then **Links → Musicbrainz** under its title.

1. If MusicBrainz has a release for it with no **Status**, and the release came from a label or a store (a label and catalogue number, a barcode, or a store link), edit the release and set **Status** to Official, with a note saying why. Once the metadata source picks the change up, the album's next refresh gives it releases, clears the mark and the warning, and Lidarr can search for it.
2. If its only releases have no audio tracks (a video, say), or MusicBrainz no longer has it, delete the album in Lidarr. It has no files, so nothing on disk changes.

Tests: refresh cases for an album with files or added by hand (kept and marked when not found, with no valid releases, or unlisted and gone) and an album without files (deleted when not found, with no valid releases, unlisted without asking the source, or marked before), keeping the first date and clearing it, publishing both changes, develop's constructor; an artist without files deleted with its folder left; the repository query, leaving out albums with files; the health check (artist names, every album sorted, the link); deleting an album (an artist with no folder or an empty one, a track file already gone, an album folder missing or empty); the default bodies. Each was checked to fail with its rule broken. develop's tests for an album without valid releases and an artist without files are back as develop has them.

## Summary by CodeRabbit

* **Bug Fixes**
  * Album health warnings now exclude albums that have files, and deleting an album skips track files and folders that are already missing.
* **Improvements**
  * The health warning lists every affected album, sorted by artist, instead of limiting the list.
  * Empty album and artist folders are still cleaned up during deletion.
* **Documentation**
  * Updated album guidance with advice on finding releases through Lidarr and deciding whether to mark a release Official or delete an album.
