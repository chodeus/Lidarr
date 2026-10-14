# Add Deezer as a built-in metadata source

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`. Builds on [`feat/metadata-source-ids`](https://github.com/chodeus/Lidarr/tree/feat/metadata-source-ids). Reviewed in [#34](https://github.com/chodeus/Lidarr/pull/34).

- Deezer as a built-in metadata source, written from Deezer's public API (no account needed). IDs are `deezer:<number>`; only numeric IDs are accepted, since Deezer answers `artist/abc` with some artist.
- Deezer reports errors, including not-found, as HTTP 200 with an error object: code 800 is a genuine not-found, anything else (quota, server) counts as Deezer being down. Requests are spaced 110 ms apart to stay under Deezer's 50 per 5 seconds; lists are read 100 at a time.
- Artist info lists the artist's albums filtered by the metadata profile. Album info carries one official release with its tracks, numbered across discs from `disk_number` and `track_position`. Record types map to Album, EP and Single; `compile` is an Album with the Compilation type.
- Search by name, by `deezer:<id>`, and album search by artist and title through Deezer's field search. Album search results lack the release date, so each is read in full (10 at most).
- `MetadataProfileAlbumFilter` holds the metadata profile's album type and status rules; `SkyHookProxy.FilterAlbums` now uses it, unchanged in behaviour, so every source filters the same way.

Tests: the API client (not-found, other errors, failed requests, paging, rate limit), ID parsing, artist and album mapping, profile filtering, search by ID and text, and the shared filter. Each rule was broken on purpose and caught by a test; the existing SkyHook filter tests also catch the filter rules. Checked live against Deezer: artist, album, search and ID lookup.

## Summary by CodeRabbit

* **New Features**
  * Added Deezer as a metadata source for artist and album lookups and searches.
  * Deezer results include album details, track listings, and artist information. Album selection follows the configured metadata profile.
  * Searches can look up Deezer IDs directly or find artists and albums by name.
* **Improvements**
  * Album metadata from Deezer is retrieved across multiple pages, with track order preserved across discs.
  * Metadata profile rules are applied consistently when filtering albums from metadata sources.
