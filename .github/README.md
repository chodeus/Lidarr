# Fixed: Delay and tagging profile ordering and missing tags

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`. Builds on [`tagging-profiles`](https://github.com/chodeus/Lidarr/tree/tagging-profiles). Reviewed in [#12](https://github.com/chodeus/Lidarr/pull/12), [#41](https://github.com/chodeus/Lidarr/pull/41) and [#50](https://github.com/chodeus/Lidarr/pull/50).

Delay and tagging profiles share their ordering code, and both had the same gaps:
- **Reorder.** Moving a profile after itself, or after the default, left gaps or a duplicate `int.MaxValue`. `Add` numbered new profiles by row count, so a gap produced a duplicate. Delay's `Reorder` also left the best-for-tags cache stale for 30 seconds.
- **The drag action.** It found the "after" profile by its order number, which fails once there is a gap. The delay action also sent `after=`, which the API ignores (it reads `afterId`), so a dragged delay profile always moved to the top.
- **Update.** A client could set `Order` directly and bypass `Reorder`.
- **Missing tags.** A create or update without `tags` threw in `ToModel` and returned a 500.

`ProfileOrder` now owns the rules for both services: `Next` for a new profile, `Reorder` (directly after the given profile, first when there is none or it is unknown, last when it is the default, no change for the default or a self-move), and `Renumber` (1..n with the default last). `Update` keeps the stored order and pins the default. `ToModel` treats missing tags as none. Both drag actions pick the "after" profile by position and send `afterId`.

The unused-tag cleanup also keeps tags that only tagging profiles use, and tags that only a root folder adds by default (develop misses those too). `TagDetails.TaggingProfileIds` starts empty, so a `TagDetails` built by a plugin that sets only develop's lists no longer makes `InUse` throw.

Tests: `ProfileOrderFixture` covers each reorder case, plus Add/Update/cache tests in both service fixtures and a resource test for missing tags (the API test project now references `Lidarr.Api.V1`). Twelve mutations, all of which build, each fail a test. The existing delay reorder tests pass unchanged. Two cleanup tests cover the kept tags; each fails without its fix. `TagDetailsFixture` checks a `TagDetails` with only develop's lists set is not in use, and fails when the list starts unset.

## Summary by CodeRabbit

* **Bug Fixes**
  * Delay and tagging profiles now maintain consistent ordering when profiles are added, updated, deleted, or reordered. The default profile remains last, and unchanged reorder requests no longer alter the list.
  * Profiles with missing tag data now load with no tags instead of failing during conversion.
