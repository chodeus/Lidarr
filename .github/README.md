# Add import list tags to artists already in the library

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`. Builds on [`feat/import-list-clean-library`](https://github.com/chodeus/Lidarr/tree/feat/import-list-clean-library). Reviewed in [#47](https://github.com/chodeus/Lidarr/pull/47).

- New import list option, Tag Existing (off by default): the list's tags are added to artists already in the library, including the artist of a listed album that exists.
- Tags are gathered across all lists and written once at the end of the sync, only for artists that lack them. Only the tags column is written (`IArtistService.UpdateTags`, one batched `SetFields`, with a default body for services built against develop).
- A new artist on two lists, or the artist of a new album on two lists, gets both lists' tags. Each new artist gets its own copy of a list's tags, so one list's tags never reach another list's artists.
- The option is in the list's edit modal and in bulk edit. Migration 910 adds `ImportLists.TagExisting`.

Tests: an existing artist tagged, nothing written with the option off or when the artist has the tags, the artist of an existing album tagged, a new artist on two lists, a new album on two lists (new and existing artist), the tags-only write against the database, and the migration default. Each rule was broken on purpose and caught by a test.

## Summary by CodeRabbit

* **New Features**
  * Import lists can now apply their tags to artists already in your library, including artists associated with albums on the list.
  * Enable or disable this behavior for each import list in its settings. When updating a list without changing this option, its current setting is preserved.
  * Tags from multiple lists are combined, and existing artist tags are retained.
