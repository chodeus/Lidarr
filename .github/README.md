# Fixed: Re-grabbing a release that was imported into a different album

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`. Reviewed in [#9](https://github.com/chodeus/Lidarr/pull/9).

When a grabbed release imports every file into a different album than the one it was grabbed for, the wanted album stays missing. The next search grabs the same release again and imports it into the wrong album again, in a loop.

`ImportedToDifferentAlbumSpecification` (a permanent rejection, database priority) looks at the last grab of the same release for each wanted album within 14 days:
- The same release is a torrent with the same info hash, or any release with the same title, as in `AlreadyImportedSpecification`.
- If the files of that grab imported only into albums other than the ones it was grabbed for, the release is rejected. Only that grab's albums count: a torrent keeps its info hash when it is grabbed again, so an older grab of it for another album doesn't make an import there look intended. An import into an album deleted since still counts until housekeeping removes its history.
- A grab with no imported files, or one that imported into a grabbed album, is not affected.
- The check is skipped when Completed Download Handling is off.

The 14-day window stops a daily loop; automatic search tries the release again 14 days after the grab, and interactive search can grab it sooner, for example once the metadata is corrected. A different upload with the same title is rejected too, as `AlreadyImportedSpecification` treats it.

`ImportedToDifferentAlbumSpecificationFixture` covers the rejection, both same-release matches, the window, partial and correct imports, and CDH off. `ImportedToDifferentAlbumHistoryFixture` goes through the real history service and repository over the test database: an import into another album, or another artist's album, is rejected; so is one after an old grab of the same torrent for the album it went into, and one into an album deleted since; a grab for several albums that imported into one of them is accepted. Counting every grab of the torrent, matching only the exact grab time, and reading history through the artist and album joins, in the spec or in the repository, each fail a test.

## Summary by CodeRabbit

* **New Features**
  * Recent grabs are now checked against import history to prevent a release from being accepted when its tracks were imported into albums outside that release. The check considers the most recent matching grab from the past 14 days and can match by torrent information or release title. Releases remain eligible when the imported albums overlap the release, the latest grab has no relevant import history, the grab is older than 14 days, or completed-download handling is disabled.
