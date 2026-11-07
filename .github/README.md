# Fixed: Album images null from an API add, blank remote url on a stored cover

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`. Reviewed in [#80](https://github.com/chodeus/Lidarr/pull/80).

An API client that left `images` out of `POST /api/v1/album` sent null. For an album with no cover art Lidarr kept it, and the insert failed on `Albums.Images` (NOT NULL), answered as 409. A stored cover that still had only the old `url` key logged `UriFormatException: The URI is empty` on every refresh.

- `AddAlbumService` gives the added album an empty image list, and deletes the import list exclusions only after the insert, so a failed add keeps them
- `EnsureAlbumCovers` and `EnsureArtistCovers` skip the download for a blank remote url; artist covers are still resized, as after a failed download
- develop's public surface is unchanged

Summary by CodeRabbit:

- Albums added without artwork now retain the source album's artwork when available.
- Adding an album no longer removes its exclusions if the add fails.
- Cover refreshes now handle missing remote image URLs without making unnecessary network requests, while still resizing existing artist artwork.

Tests: `AddAlbumServiceDbFixture` adds through the real `AlbumService`, `AlbumRepository` and `ImportListExclusionService`. An album posted without images saves an empty list, or the source's covers. A successful add removes its album and artist exclusions and keeps an unrelated one. A failed add keeps all three. `MediaCoverServiceFixture` parses stored covers with the database's `EmbeddedDocumentConverter`: an album cover with only `url`, or a blank `remoteUrl`, is never requested; an artist cover is not requested but is still resized. Each fix, reverted, fails its test.
