# New: Import list clean library option

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`. Reviewed in [#17](https://github.com/chodeus/Lidarr/pull/17).

Ported from Sonarr's import list "Clean Library Level" (68c326a, with the matching fixes from 7d77500 and 2b2b973), in Sonarr's names and structure: `ListSyncLevelType` (Disabled, Log Only, Keep and Unmonitor, Keep and Tag), `ListSyncTag`, the `ImportListItems` store, `ImportListStatus.HasRemovedItemSinceLastClean`, `AllListsSuccessfulWithAPendingClean`, `MarkListsAsCleaned`, the `config/importlist` endpoint and the Options section on the Import Lists settings page.

- The default is Disabled, so nothing changes until the option is set. Like Sonarr, the Options section only shows with advanced settings on.
- Each list's items are stored after a fetch that didn't fail. A clean runs only when every automatic-add list has synced, none is in a failure backoff, and at least one list has dropped an item since the last clean.
- Plugins build import lists against develop, so `IImportList.Fetch()`, `IFetchAndParseImportList` and the existing `IImportListStatusService` members keep their develop signatures. Sonarr changed `Fetch()` to return `ImportListFetchResult`; here the fetch service reads each list's `MostRecentFailure` before and after the fetch instead. Lidarr's lists record a failure there when they catch an error, and a list that throws is never stored.
- A list that failed still adds its items and updates its sync time, as on develop. Only the stored items wait for a clean fetch.
- Migration 905 adds the `ImportListItems` table and the status column (900+ is this build's range).

Changes from Sonarr, because Lidarr lists hold artists and albums:
- An item's identity is the artist, album and both MusicBrainz IDs. That is the same key the fetch already de-duplicates on.
- An artist counts as listed if an item has its MusicBrainz ID or an old ID, its cleaned name, or the ID (or old ID) of one of its albums. This way album lists keep their artists, and so do name-only items the lists could not map.
- Cleaning works on artists only. Albums keep their monitoring.

Tests: the clean levels, the gates (disabled, list failure, never synced, no removals, removals on any list), each "still listed" match, single-list sync, list deletion, the item store's insert, update and delete, the fetch service storing only lists that didn't fail and setting the removed flag, and the status flag, including develop's `UpdateListSyncStatus` overload. Twenty-one mutations, all building, each fail a test.

## Summary by CodeRabbit

* **New Features**
  * Added import-list cleanup settings to disable cleanup, log affected artists, unmonitor them, or apply a tag.
  * Configure tag selection for the keep-and-tag option.
* **Improvements**
  * When enabled, cleanup handles artists no longer represented in import lists while preserving artists still present on another list.
  * Import-list items now synchronize after successful fetches, and cleanup can run after items are removed or a list is deleted.
