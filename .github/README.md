# Keep Discogs answers in a disk cache beside the database

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`. Builds on [`feat/discogs-metadata-source`](https://github.com/chodeus/Lidarr/tree/feat/discogs-metadata-source). Reviewed in [#36](https://github.com/chodeus/Lidarr/pull/36).

- Discogs answers are kept in `discogs-cache.db` beside the database (not in `lidarr.db`, so backups don't grow). Values are the trimmed resources Lidarr reads, as gzip JSON, about 0.6 KB per release.
- Releases and masters are kept 30 days; artists, artist discographies and pressing lists 7 days. Searches are never cached, and a not-found or an outage is never stored. A list page without pagination or a page count, or an artist, master or release without an ID, counts as Discogs being down, so it is never stored. Keys carry a fingerprint of the Discogs resource fields, so a build that adds or drops a field never reads entries an older build wrote.
- A housekeeping task removes expired entries. A cache that can't be opened or read is skipped with a warning; the lookup goes to Discogs. A cache file deleted while Lidarr runs is made again on the next lookup.
- The source's one-day in-memory cache of album types is gone; the disk cache covers it.

Tests: round trip, expiry, housekeeping, entries written for other resource fields, a deleted cache file, an unusable cache, a list page without a page count, a list of no pages, an answer without an ID, cache hits, the key and lifetime of each cached lookup, what is and isn't stored, and searches. Each rule was broken on purpose and caught by a test. Checked live: the first lookup took 677 ms, the cached one 2 ms.

## Summary by CodeRabbit

* **New Features**
  * Discogs metadata lookups are cached to reduce repeat requests. Cache entries expire automatically, with different lifetimes for different types of metadata.
* **Bug Fixes**
  * Unavailable or unsuccessful lookups aren’t cached, so later requests can try again.
  * Expired or incompatible cached data is ignored, and cleanup preserves unexpired entries.
  * Discogs list requests now report unavailable results when pagination information is missing, rather than treating the list as complete.
