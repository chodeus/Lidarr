# New: Import list clean library option

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`. Reviewed in [#17](https://github.com/chodeus/Lidarr/pull/17).

Ported from Sonarr's import list "Clean Library Level" (68c326a, with the matching fixes from 7d77500 and 2b2b973), in Sonarr's names and structure: `ListSyncLevelType` (Disabled, Log Only, Keep and Unmonitor, Keep and Tag), `ListSyncTag`, the `ImportListItems` store, `ImportListStatus.HasRemovedItemSinceLastClean`, `AllListsSuccessfulWithAPendingClean`, `MarkListsAsCleaned`, the `config/importlist` endpoint and the Options section on the Import Lists settings page.

- The default is Disabled, so nothing changes until the option is set. Like Sonarr, the Options section only shows with advanced settings on.
- Each list's items are stored after a fetch that didn't fail, except for a list deleted while it was fetched. A clean runs only when every automatic-add list has synced, none is in a failure backoff, and at least one list has dropped an item since the last clean. It clears only the flags it read, so a list that drops an item while it runs is cleaned next time.
- It fails closed: nothing is cleaned while a stored item can't be tied to an artist by an id or by an album in the library, since its artist would be matched by name alone. The sync maps each item to its artist, and the stored item keeps that id when it came from a lookup by album id. An artist found by a title or name search could be another one, so an item the sync can only place that way, such as any Last.fm album (Last.fm names albums by title), holds the clean back too; the warning names it. Items of lists whose automatic add is off don't hold it back, since nothing refreshes them.
- Spotify records a list failure when its MusicBrainz mapping service fails, and a success otherwise. Before, the outage went unrecorded and the items were stored by name only. A single item that fails to map is stored without its ids, which holds a clean back without putting the whole list in backoff.
- Plugins build import lists against develop, so `IImportList.Fetch()`, `IFetchAndParseImportList` and the existing `IImportListStatusService` members keep their develop signatures. Sonarr changed `Fetch()` to return `ImportListFetchResult`; here the fetch service reads each list's `MostRecentFailure` before and after the fetch instead. Lidarr's HTTP lists record a failure there when they catch an error, Spotify now does when its mapping fails, and a list that throws is never stored.
- A list that failed still adds its items and updates its sync time, as on develop. Only the stored items wait for a clean fetch.
- Migration 905 adds the `ImportListItems` table and the status column, and 913 adds the item's mapped artist id (900+ is this build's range). A housekeeper deletes the items of lists that no longer exist.
- An item's identity ignores case, so a provider changing a name's case isn't a removal, and dates without a zone are stored as UTC, so an unchanged item isn't rewritten on every sync.
- The clean reads each artist again just before it writes, so an edit made while the lists were compared isn't overwritten. Keep and Tag needs a tag that exists, when saved and when the clean runs.

Changes from Sonarr, because Lidarr lists hold artists and albums:
- An item's identity is the artist, album and both MusicBrainz IDs. That is the same key the fetch already de-duplicates on.
- An artist counts as listed if an item has its MusicBrainz ID or an old ID, the artist ID the sync mapped the item to, its cleaned name, or the ID (or old ID) of one of its albums. This way album lists keep their artists.
- Cleaning works on artists only. Albums keep their monitoring. As in Sonarr, it covers every artist in the library, including ones added by hand; the help text says so.
- A clean triggered by deleting a list leaves that list's items out, since they may not be removed yet when it runs. Leaving the Import List Options page clears its unsaved changes.

Tests: `ImportListCleanLibraryDbFixture` (re-audit, 2026-10) runs the real fetch service, Spotify list, item store, status, artist and album services over the test database: a followed artist stays monitored while Spotify's mapping is down and the clean runs again once it's back, the artist of a listed album that isn't in the library stays monitored, the artist the sync mapped an album item to is kept while an unlisted one is cleaned, a list deleted during its fetch stores nothing, a case-only change isn't a removal, and an unchanged item isn't rewritten. `ImportListCleanLibrarySyncedListsFixture` adds: an item placed only by a title search (a parsed Last.fm album, and one with no ids) holds the clean back, an item of a list that no longer syncs doesn't, and one failed Spotify item lookup doesn't put the list in backoff. Also: no clean while an item matches no artist, the mapped artist kept, no tagging with a deleted tag, only the read flags cleared, the artist written as read again, and the orphan housekeeper. Before that: the clean levels, the gates (disabled, list failure, never synced, no removals, removals on any list), each "still listed" match, single-list sync, list deletion, the item store's insert, update and delete, the fetch service storing only lists that didn't fail and setting the removed flag, and the status flag, including develop's `UpdateListSyncStatus` overload. Thirty-six mutations, all building, each fail a test. The tests that log a warning or an error expect it, so they also pass in a Debug build, which fails a test on an unexpected log entry; the outage test ignores its errors, whose count depends on what earlier tests left cached.

## Summary by CodeRabbit

* **New Features**
  * Added import-list cleanup settings to disable cleanup, log affected artists, unmonitor them, or apply a tag.
  * Configure tag selection for the keep-and-tag option.
* **Improvements**
  * When enabled, cleanup handles artists no longer represented in import lists while preserving artists still present on another list.
  * Import-list items now synchronize after successful fetches, and cleanup can run after items are removed or a list is deleted.
