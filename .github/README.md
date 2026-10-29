# Fixed: Import list albums whose artist has a blank MusicBrainz id

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`.

Last.fm's top albums give each album's artist with the artist's MusicBrainz id, and send `""` for an artist Last.fm has no id for. The sync looks such an item up by the album's name and fills in the artist from the album it finds, but develop fills it with `??=`, which leaves the `""` in place. `ProcessAlbumReport` then drops the item because its artist id is blank, so the album is never added. In the custom build, `feat/import-list-clean-library` also holds its clean while any list item has no artist ("Not cleaning the library: … list items aren't matched to an artist"), so one such item stops the clean on every sync.

- `MapAlbumReport` treats a blank artist name or id like a missing one, as the rest of the sync does with `IsNullOrWhiteSpace`, so the album is added under the artist of the album it found. An item with only an artist already got this in `MapArtistReport`.

Tests: an album item with a blank artist id, with and without the artist's name, is added under the found album's artist and its name. Each of the two lines was put back to develop's check on purpose and caught by a test.

Full Core suite in Release: 3,249 pass. Merged after the custom build's 53 other branches, it merges cleanly, also beside `feat/import-list-tag-existing`'s change to the method below `MapAlbumReport`, and builds, `en.json` has no repeated keys, and the Core suite there passes 4,689. In Debug, only the three tests that also fail without this branch fail: `should_remove_missing_import_lists_on_startup` and two `UpgradeMediaFileServiceFixture` tests for a file gone from disk.
