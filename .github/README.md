# Do not blocklist similar NZBs from a different indexer

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`. Reviewed in [#38](https://github.com/chodeus/Lidarr/pull/38).

- A Usenet blocklist entry now matches a release only from the same indexer, with the same published date. An entry with no indexer still matches that date from any indexer.
- Before, an entry also matched a release from another indexer with the same published date, or within 2 minutes of it and within 2 MB in size.
- A failed post listed on several indexers can now be grabbed from each of them once before every copy is blocklisted.

Tests: another indexer (same and near date), the same indexer (in any case, same date, another date, an entry with no date), and an entry with no indexer. Each rule was broken on purpose and caught by a test.

## Summary by CodeRabbit

* **Bug Fixes**
  * Blocklist matching now requires an exact published-date match along with a valid indexer match. Releases with different or missing publication dates no longer match based on a nearby date or similar file size.
  * This reduces incorrect matches between distinct releases while allowing entries without an indexer to match when their published dates are identical.
