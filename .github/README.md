# Show grabbed and blocklisted releases in Interactive Search

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`. Reviewed in [#43](https://github.com/chodeus/Lidarr/pull/43).

- Interactive Search shows a History column. A release grabbed before has a download icon with the grab time, red with the failure time when that download failed. A blocklisted release has a blocklist icon.
- The release API returns `history` (grabbed and failed dates) and `blocklisted`.
- A grab matches by GUID, then by torrent info hash when the release has one, otherwise by the same title, indexer and published date. A failure belongs to a grab by download ID, unless it was recorded before that grab: a torrent grabbed again keeps its download ID.
- Only grab and failure events are loaded: the album's for an album search, the artist's for an artist search. RSS results have no history.
- `BlocklistSpecification.BlocklistedReason` names the rejection that sets `blocklisted`.

Tests: matching by GUID, info hash (any case), and title, indexer and date; another hash, indexer or date not matching; the newest grab used; a failure tied by download ID only, and not to a later grab; history data keys read back from the database in camelCase; the blocklist rejection reason. Each rule was broken on purpose and caught by a test.

## Summary by CodeRabbit

* **New Features**
  * Release search results include a History column showing when a release was grabbed and, if applicable, when its download failed.
  * Blocklisted releases are marked with an icon in search results.
