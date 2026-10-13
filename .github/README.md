# Faster library, Wanted and history queries

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`. Reviewed in [#27](https://github.com/chodeus/Lidarr/pull/27).

- Album, track, calendar and artist lists load their artists, releases and albums in batches instead of one query per row
- Wanted Missing and Cutoff Unmet use `EXISTS` instead of joining every track and grouping
- The history count no longer joins Tracks
- Root folders are read once per artist list and health check
- Album lists link statistics only for the artists they return
- Migration 907:
  - Tracks `(AlbumReleaseId, TrackFileId)` replaces `(AlbumReleaseId)`
  - History `(EventType, Date)` replaces `(EventType)`, and `(ArtistId, AlbumId)` is added
  - New indexes on Blocklist, PendingReleases, ExtraFiles, LyricFiles and MetadataFiles
  - Drops `AlbumReleases (Monitored)`
- API responses and public signatures are unchanged; new interface members have default bodies

Measured on a copy of one library (1,116 artists, 1.05M tracks across all releases, 940k history rows), develop plus every custom-build branch, before and after. The Postgres 16 copy holds the same rows.

| Endpoint | SQLite before | SQLite after | Postgres before | Postgres after |
|---|---|---|---|---|
| `track?artistId=` (one artist, 88,896 tracks) | 27.7 s | 0.78 s | 61.3 s | 0.67 s |
| `album?artistId=` (one artist, 5,020 albums) | 1.95 s | 0.18 s | 2.48 s | 0.19 s |
| `album` (51,915) | 4.28 s | 2.55 s | 4.06 s | 2.19 s |
| `artist` | 334 ms | 214 ms | 383 ms | 164 ms |
| `history` page | 516 ms | 66 ms | 112 ms | 78 ms |
| Wanted Missing, page 3 | 405 ms | 175 ms | 1.10 s | 54 ms |
| Wanted Cutoff Unmet, page 1 | 674 ms | 577 ms | 722 ms | 507 ms |

- Migration 907: about 2 s on SQLite, 1.5 s on Postgres
- Every response matched field for field on both databases
- Row order differs in artist track and track file lists, and among Wanted albums that share a release date, including across a page boundary

Tests cover the Wanted queries, batch loading against lazy loading, the root folder overload, the interface defaults and the paged history count; each fails when its rule is broken. All custom-build branches merge with this, and the merged tree's only test failures (three hardlink tagging tests and a process-start timing test) fail without it too.

## Summary by CodeRabbit

* **Bug Fixes**
  * Improved album and track details in API responses, including artist, release, and statistics information.
  * Corrected missing-album and unmet-cutoff results, their counts, and filtered history totals.
  * Improved artist matching in calendar results and root-folder path resolution.
* **Performance**
  * Reduced repeated lookups when loading album, artist, track, and root-folder information.
  * Improved database query performance for album and history requests, with additional database indexes.
