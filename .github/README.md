# Fixed: Albums an artist refresh adds staying empty when the changed-album list leaves them out

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`. Reviewed in [#52](https://github.com/chodeus/Lidarr/pull/52).

- A scheduled artist refresh asks the metadata source which albums changed since the last run. When that list isn't empty, `RefreshAlbumInfo` refreshes only the albums on it.
- The artist refresh inserts albums new to the artist's list (`AddChildren`) before that step. A new album has no releases or tracks until its first refresh, and nothing else refreshes it. When the changed list leaves it out, it is skipped and stays empty.
- It then stays empty until it appears on a later changed list, a run gets no list and falls back to the time-based check, or someone refreshes it by hand. The new album is monitored like any other, so it is searched with no tracks.
- Seen on the live library: one scheduled run added 43 albums across 15 artists and refreshed none of them. The source's changed list for that window held 5,976 albums and none of the 43.
- Now an album that has never synced (`LastInfoSync` empty or the minimum date) is always refreshed, whatever the changed list says. Synced albums still follow the list.

Not changed:

- Albums that already synced, artists the refresh skips, and forced refreshes behave as before. An empty album whose artist is skipped as unchanged is not reached; it needs a refresh of its own.
- A never-synced album the source has no usable copy of is refreshed again on each run that reaches it. The refresh then decides what happens to it, as it does today when one is refreshed.

Tests: a never-synced album missing from the changed list is refreshed, with `LastInfoSync` null and with the minimum date; a synced album missing from the list is still skipped. Removing the fix fails both never-synced cases; treating only null, only the minimum date, or every album as never synced each fails a test. `MusicTests` and `MetadataSource` pass, apart from one live metadata API test that timed out.

## Summary by CodeRabbit

* **Bug Fixes**
  * Albums that have never been synced are now refreshed even when they are not listed as changed by the metadata provider. Albums with a previous sync are still skipped when they are absent from that list.
