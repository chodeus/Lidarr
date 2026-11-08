# New: This build's changes on the Updates page

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`. Custom-only, never submitted upstream. Reviewed in [#81](https://github.com/chodeus/Lidarr/pull/81).

System → Updates listed upstream's nightlies, none marked installed, so it never said what the running build changed. The image's `changelog.py` now writes `changelog.json` beside the binaries: the image's last five builds, each with the branches it added or removed and upstream's own `New:` and `Fixed:` lines since the build before.

- `RecentUpdateProvider` returns the file's entries when it has any, and asks the update service otherwise, as develop does
- The reader keeps one entry per version and skips entries without one, since `UpdateController` sorts by version and looks the running build up with `SingleOrDefault`
- A malformed or unreadable file logs a warning and falls back to the update service
- `RecentUpdateProvider` keeps develop's constructor for plugins; the container picks the new one with `IBuildChangelog`

Summary by CodeRabbit:

- Recent update listings can now display versions and release details from the build's changelog. If no usable changelog is available, listings continue to come from the update service.
- Duplicate versions are shown only once, and entries without a version are excluded.
- Changelog problems, such as unreadable or malformed content, no longer prevent recent updates from being displayed.

Tests: `BuildChangelogFixture` reads a real `changelog.json` through Lidarr's JSON parser: present, missing, malformed, unreadable, a repeated version, entries without a version, and no entries. `BuildChangelogUpdatesFixture` checks the page through the real `UpdateController`, `RecentUpdateProvider` and reader. `ContainerFixture` pins the constructor the container picks. Each fix, reverted, fails its test. The built app, stamped 10.0.0.162, served the generated file from `/api/v1/update` with that build marked installed.
