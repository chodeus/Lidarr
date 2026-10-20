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

Second round, from an audit of the branch:

- **Adding after an old max-order profile.** An older reorder could leave a tagged profile at `int.MaxValue`. `Next` then overflowed, and the new profile sorted first. `ProfileOrder.MakeRoom` now renumbers first when a tagged profile is at or next to `int.MaxValue`. Otherwise `Add` still leaves existing rows alone.
- **Lost updates.** `Reorder`, and the renumber after `Delete`, wrote every column of every row. A save that landed in between was undone, for example the default tagging profile's audio tag setting from Settings > Metadata. They now write only `Order`. `Add`, `Update`, `Delete` and `Reorder` are also serialised per service, so an `Update` can't write back an order that a concurrent `Reorder` has just changed.
- **Delete.** If the renumber failed after the delete committed, the best-for-tags cache kept serving the deleted profile for up to 30 seconds. The cache is now cleared either way.
- **Tags page.** Tag details from the API left out root folders. A tag used only as a root folder default showed "No links", and its Delete button failed with a 409. Root folders now count, and they show on the card and in the details.
- **Profile list after a delete.** The server renumbers the remaining profiles, but the page kept the old orders, so the next added profile could show in the wrong place until a reload. Both lists now reload after a delete.

Tests: `DelayProfileServiceDbFixture` and `TaggingProfileServiceDbFixture` run the real services over the test database. The races are injected at the repository, and the Update/Reorder race runs on two threads. Each new case failed before the fix. Four mutations (always renumbering, the overflow threshold, and each lock) each fail a test. `TagDetailsResourceFixture` covers the root folder mapping. Full Core suite: 3284 pass, and the 3 hardlink tests that fail on this Mac fail as before.

Third round, from a further audit of the branch:

- **Lookups racing a change.** A lookup that missed the cache read the profiles, then stored what it read. A reorder, delete or save that cleared the cache in between left the old ranking cached for 30 seconds. A miss now reads and stores under the writers' lock; a hit is served without it, since RSS sync and tagging look profiles up per release and per file. The tagging service's seeded default shares the cache and the same rule.
- **Add after a failed insert.** Add's renumber commits before the insert, so when the insert failed the cache kept the old orders. Add now clears the cache either way.
- **A default stored out of place.** An older save could leave the default at a low order, and an older reorder a tagged profile tied with it at `int.MaxValue`. Either way the default won every lookup. Lookups now rank the default last whatever its stored order, and `MakeRoom` renumbers on the next add.

Tests: both DB fixtures run these against the real database: a reorder and a delete landing between a lookup's read and its store (on two threads), a failed insert after a renumber, a default stored out of place, a tagged profile at the maximum order, and cached lookups that must not wait for a writer or for another lookup's read. The tagging fixture also covers the seeded default. Each new case failed before its fix, except the four timing tests, which check that hits aren't serialised. Ten mutations each fail a test. Full Core suite: 3302 pass, and the 3 hardlink tests that fail on this Mac fail as before. Merged after the custom build's other branches, it builds, the plugin check passes, and the suite there passes 4143 and fails the same 3.

## Summary by CodeRabbit

* **Bug Fixes**
  * Delay and tagging profiles now maintain consistent ordering when profiles are added, updated, deleted, or reordered. The default profile remains last, and unchanged reorder requests no longer alter the list.
  * Profiles with missing tag data now load with no tags.
  * Drag-and-drop reordering now follows the displayed profile positions.
  * Tags used by tagging profiles or added by default through root folders are retained.
* **New Features**
  * Tag details now show associated root folders and their names or paths.
