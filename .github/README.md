# New: Track file relative path in the API

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`. Reviewed in [#61](https://github.com/chodeus/Lidarr/pull/61).

The track file API (`/api/v1/trackfile`) gives each file's `relativePath`, its path relative to the artist's folder, as Sonarr's and Radarr's file resources do. `path` stays the absolute path. Both paths are normalized first, so a doubled separator or a `.` segment in either path doesn't shift the cut (`GetRelativePath` cuts at the parent's raw length). `relativePath` is null when the artist isn't loaded (unmapped files) or the file is outside the artist's folder; a folder whose name only starts with the artist folder's ("Artist Two" beside "Artist") counts as outside. The frontend's `TrackFile` type gains an optional `relativePath`.

`TrackFileResourceFixture` maps through the real resource mapper and path helpers: a file in the artist folder (plain, with a trailing separator, doubled separators, `.` segments in either path), a file in a sibling folder, and a file mapped without its artist. Each rule was broken on purpose and caught by a test.

## Summary by CodeRabbit

* **New Features**
  * Track file information now includes an optional path relative to the artist’s folder.
  * Relative paths are provided when the file is within the artist directory; files outside it do not receive a relative path.

* **Tests**
  * Added coverage for normalized paths and files located outside the artist directory.
