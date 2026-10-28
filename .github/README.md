# Add excluded tags and multiple indexers to release profiles

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`. Builds on [`metadata-profile-must-not-contain`](https://github.com/chodeus/Lidarr/tree/metadata-profile-must-not-contain), the top of the metadata stack, so the tag screens and tag tests include every profile type. Reviewed in [#49](https://github.com/chodeus/Lidarr/pull/49) and [#70](https://github.com/chodeus/Lidarr/pull/70).

- Release profiles get Excluded Tags: a profile does not apply to an artist with any of them. A tag cannot be in both Tags and Excluded Tags, and `excludedTags: null` is rejected.
- A release profile can list several indexers instead of one. An empty list means every indexer. Migration 911 adds `ExcludedTags`; migration 912 adds `IndexerIds`, filled from `IndexerId` in one update.
- The API keeps `indexerId` for older clients: `indexerIds` wins when sent, `indexerId` alone becomes a one-indexer list, and `0` means every indexer. Responses carry both, with the first listed indexer as `indexerId`. A client that sends back only `indexerId`, equal to the first listed indexer, keeps the stored list; a negative indexer id is rejected rather than read as every indexer.
- `ReleaseProfile.IndexerId` stays for plugins, read from and written to `IndexerIds`. It is still stored, so an older build reading the table keeps a one-indexer profile on its indexer; reads set it before the later `IndexerIds` column, so the full list wins. A row an older build added or changed (only `IndexerId` written) is read by its `IndexerId`, `0` (every indexer) included, since this build always stores its first listed indexer there. The controller keeps develop's constructor.
- A tag used only as an excluded tag counts as in use: the tag screen lists the profile, the tag cannot be deleted, and the unused-tag cleanup keeps it.
- The settings card shows excluded tags in red and every listed indexer.
- Deleting an indexer takes it out of every release profile; a profile left with no indexer is disabled with a warning, rather than widened to every indexer. Only the indexer columns are written, and `Enabled` for a profile it disables, in the same statement, so a profile edit saved meanwhile keeps its other fields.
- The new `IReleaseProfileService.AllExcludedForTag` has a default body, and `TagDetails.ExcludedReleaseProfileIds` starts empty. The unused `IndexerIdHelpText` string is removed.

Tests: tag filtering with excluded tags, the indexer filter, tag details and the delete guard, the cleanup, the repository round trip (the full list read back, the first indexer in the legacy column), develop's `IndexerId`, a new `TagDetails` list, the migration, the API mapping (including `indexerId` 0) and validation (including null excluded tags and negative indexer ids), a client sending back only the first indexer, rows an older build added or changed (real database), and a deleted indexer, also with a profile edit saved meanwhile. Each rule was broken on purpose and caught by a test.

## Summary by CodeRabbit

* **New Features**
  * Release profiles can now apply to multiple indexers. Profiles without a selected indexer remain available across all indexers.
  * Release profiles can exclude tags, preventing them from applying when matching tags are present.
  * Tag details show when a tag is referenced by an excluded release profile.

* **Bug Fixes**
  * Tags used by excluded release profiles are retained and can’t be deleted while still in use.
  * Release profile settings reject overlapping included and excluded tags.
