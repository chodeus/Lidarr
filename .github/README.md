# Seek an artist's track files instead of scanning them all

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`. Reviewed in [#28](https://github.com/chodeus/Lidarr/pull/28).

- `GetFilesByArtist` also filters `TrackFiles.AlbumId` on the artist's album ids

Measured on a copy of one library (109k track files), before and after:

| `trackfile?artistId=` | SQLite before | SQLite after | Postgres before | Postgres after |
|---|---|---|---|---|
| Artist with 5 files | 46 ms | 2 ms | 5 ms | 5 ms |
| Artist with 8,739 files | 632 ms | 599 ms | 584 ms | 541 ms |

`GetFilesByArtist` for all 1,118 artists, query only:

| | Before | After |
|---|---|---|
| SQLite | 70.3 s | 0.22 s |
| Postgres | 1.0 s | 1.35 s |

- Same 108,581 rows on both databases, with and without migration 907
- Postgres difference is planning time (about 0.3 ms per call); execution time is unchanged

Test: an artist with two albums beside another artist's album; it fails when the album filter is wrong.

## Summary by CodeRabbit

* **Bug Fixes**
  * Corrected artist file listings to include tracks from all albums by the requested artist while excluding files from other artists.
