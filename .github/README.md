# Add a supplementary role that only adds albums the artist lacks

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`. Builds on [`feat/discogs-disk-cache`](https://github.com/chodeus/Lidarr/tree/feat/discogs-disk-cache). Reviewed in [#37](https://github.com/chodeus/Lidarr/pull/37).

- A third source role, Supplementary, that only adds albums the artist doesn't have and never changes other sources' albums.
- The supplement finds the artist through the primary artist's links (MusicBrainz lists Discogs and Deezer artist pages) via a new `IMetadataSource.IdFromLink`; there is no name search. A supplement of the same ID kind uses the artist's own ID. No link means the supplement adds nothing.
- A supplement with other IDs matches albums by ID (merged IDs too) or by title: cleaned titles equal within a year, or one an edition ("Deluxe", "Remastered", "Collector's Edition" and similar) of the other's title, from any year. A same-kind supplement and the And combination match by ID only, merged IDs included, since the same title under another ID is another album. Before, the And merge missed an album merged into one an earlier source lists.
- Album refresh adds a new artist when a source names a different album artist, so a supplementary album would have moved to the supplement's copy of the artist. The album lookup now keeps the album, and its tracks by that artist, with the artist it was added to whenever the ID kinds differ.
- A supplement with other IDs that is down keeps the albums it added before, so a refresh removes nothing. A same-kind supplement can't tell its albums from the primary's, so its outage fails the lookup, as a down source does in an And merge. Supplementary sources answer for their own IDs when no other source can, and stay out of search. One that answered for the artist is not asked again for its albums.
- The album owner is only read from the database when a profile has tags or uses a supplementary source.

Tests: adding only missing albums, same-kind supplements, a supplement that answered for the artist, no link, not found, a supplement that is down (both kinds), answering its own IDs, keeping the owner, leaving same-kind albums alone, skipping the owner lookup, the profile flag, the matching rules (by ID and merged ID too), the And merge with merged IDs, and the Deezer and Discogs link readers. Each rule was broken on purpose and caught by a test.

## Summary by CodeRabbit

* **New Features**
  * Added a **Supplementary** role for metadata sources in profile settings. Supplementary sources can add albums missing from the artist’s existing metadata without replacing existing albums.
  * Album details from supplementary sources are checked to avoid adding duplicate albums or editions, and existing ownership and guest track information is preserved.
  * Deezer and Discogs artist links can now be recognized when looking up artists across metadata sources.
