# Fixed: Store the removed status of an artist the metadata source no longer has

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`. It is stacked on `fix/refresh-keeps-concurrent-changes` and merges after it. Reviewed in [#PR_NUMBER](https://github.com/chodeus/Lidarr/pull/PR_NUMBER).

When the metadata source reports an artist as not found, the refresh marks it Deleted. It only did so on the copy it was working on, which nothing saved, so the stored status stayed as it was. The removed-artist health check reads the stored status, so it never listed such an artist.

- The refresh saves the status on its own through `IArtistService.UpdateMetadataStatus`, which writes only the metadata's `Status`. It is a default interface method whose body stores nothing, as `develop` does, so implementations built against `develop` keep working. `ArtistService` takes the metadata repository for it.
- It clears the artist cache too, after the write, so a read of the artist list in between can't keep the old status. The cache holds each artist with its metadata for up to 30 seconds, and the health check that runs on the refresh's events reads it.
- A later refresh that finds the artist again stores the metadata source's status as before, which clears the mark.

Tests: `RefreshArtistDeletedStatusFixture` runs the refresh against the real `ArtistService` and metadata repository over the test database. The Deleted status is stored; the real `RemovedArtistCheck` reports the artist straight after the refresh, with the artist cache filled beforehand and with the artist list read while the status is written; and a later refresh that finds the artist stores its status again. Leaving out the save fails the first three, leaving out the cache clear fails both health check tests, and clearing before the write fails the second.
