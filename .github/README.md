# Update every matching Plex library

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`. Reviewed in [#42](https://github.com/chodeus/Lidarr/pull/42).

- Plex Media Server: a refresh updated only the first music library whose folder matched the artist's root folder. Every matching library is now updated.
- A library with two matching folders is still updated once, and with no match every music library is updated, as before.

Tests: two matching libraries, one match among several, two matching folders in one library, and no match. Each rule was broken on purpose and caught by a test.

## Summary by CodeRabbit

* **Bug Fixes**
  * Plex library updates now reach every matching section while avoiding duplicate updates to a section. If no locations match, all sections are still updated.
