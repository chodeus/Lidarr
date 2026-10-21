# Keep the grab history mapping when a queued download is remapped

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`. Builds on [`feat/fail-downloads`](https://github.com/chodeus/Lidarr/tree/feat/fail-downloads), which changes the same mapping code. Reviewed in [#51](https://github.com/chodeus/Lidarr/pull/51).

- A queued download is remapped when an album is removed or deleted, or an artist is added or deleted. The remap used only the client's title, so a download that first mapped through its grab history lost its artist and albums. The remap now runs the same mapping as first tracking (`MapRemoteAlbum`): the client title, the grab history fallback, release info, aggregation and custom formats.
- The mapping is built on its own objects and applied only once it is complete, so the queue and imports never see half a remap. A remap that fails at any step keeps the previous mapping and status, including an incomplete import's messages. First tracking applies what the steps mapped before a failure, with the warning, as before.
- A grab whose artist or album has been deleted since maps nothing from history: the download keeps what its title maps (the artist, with no albums) or is left unmapped, and never points at the deleted artist or album.
- A remap that maps nothing, from either the client title or the grab history, clears the mapping.
- First tracking still restores the state and, for an incomplete import, its messages from the download history. That now runs after the mapping, with its own error handling: a failed read or a bad entry loses only the state or the messages, never the mapping, and can't drop the download from the queue. A remap no longer reads the download history.
- The remap no longer needs `feat/fail-downloads`' step that kept the old release, since it rebuilds the release from the grab.
- A download with no albums mapped no longer counts as already imported. `IsImported` returned true for it: with an artist, the download was marked imported with nothing imported; without one, `VerifyImport` threw reading the artist and the download stayed in Importing.

Tests: a remap keeps the grab history mapping; a failing grab history read or title lookup keeps the previous mapping; a new download whose download history can't be read is still mapped; a deleted artist whose grab history remains leaves the download unmapped without throwing; an unparsable title is unmapped once its history is gone; an incomplete import's messages are restored with their text, and an unreadable or `null` entry still leaves the download tracked; `IsImported` is false with nothing mapped; an import with no albums mapped, with or without an artist, ends ImportFailed instead of imported or stuck. `TrackedDownloadRemapDeletedFixture` (re-audit, 2026-10) runs the real parsing, artist and album services over the test database: a deleted album or artist whose grab history remains is dropped, and the artist the title names is kept. A reader during a remap sees the previous mapping; a remap that fails after mapping keeps the previous mapping, indexer and messages; a new download whose mapping fails is tracked with a warning. Each rule was broken on purpose and caught by a test.

## Summary by CodeRabbit

* **Bug Fixes**
  * Downloads without mapped albums are no longer marked as imported, including when album information is missing or a track is rejected.
  * Existing album mappings are retained during remaps when title lookup or history reads fail; mappings are cleared when the artist is deleted or grab history is gone and the title cannot be parsed.
  * Incomplete imports can display status messages, while missing or malformed status data no longer interrupts download tracking.
