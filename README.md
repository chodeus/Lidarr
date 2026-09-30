# Lidarr custom build

`ghcr.io/chodeus/lidarr:custom` is Lidarr's `develop` branch with the changes below merged in, on the hotio base image.

## What it adds to Lidarr develop

- **Tagging profiles** — branch [`tagging-profiles`](https://github.com/chodeus/Lidarr/tree/tagging-profiles), upstream PR [#5785](https://github.com/Lidarr/Lidarr/pull/5785)
  - The Write Metadata to Audio Files settings become tagging profiles under Settings → Profiles, applied to artists by tag.
  - Each profile can skip hardlinked files (on by default), so a seeding torrent's files are never retagged.
  - Existing settings migrate into a default profile. Migration 082.
- **Delay and tagging profile ordering** — branch [`fix/profile-order-and-tags`](https://github.com/chodeus/Lidarr/tree/fix/profile-order-and-tags), builds on `tagging-profiles`, not submitted
  - Dragging a delay profile moves it where it's dropped instead of to the top, and both profile lists stay numbered 1..n with the default last.
  - Saving a profile keeps its position, and a request without tags no longer fails.
- **Artists sharing a clean name** — branch [`fix/ambiguous-artist-disambiguation`](https://github.com/chodeus/Lidarr/tree/fix/ambiguous-artist-disambiguation), upstream PR [#5840](https://github.com/Lidarr/Lidarr/pull/5840)
  - When two artists reduce to the same clean name, the parsed album picks the artist instead of failing with "found multiple artists".
- **Must Not Contain terms for metadata profiles** — branch [`metadata-profile-must-not-contain`](https://github.com/chodeus/Lidarr/tree/metadata-profile-must-not-contain), not submitted
  - Whole-word terms or regexes that keep matching albums (such as live recordings) out of an artist's albums on refresh. Albums with files or added by hand stay.
  - Migration 900.
- **Retry failed imports after a refresh** — branch [`feat/retry-import-failed`](https://github.com/chodeus/Lidarr/tree/feat/retry-import-failed), not submitted
  - When a metadata refresh changes an album, that artist's `importFailed` downloads get fresh album data and are imported again, so a MusicBrainz fix clears the queue without a manual import.
- **Allow Smaller Release Upgrades** — branch [`feat/smaller-release-upgrades`](https://github.com/chodeus/Lidarr/tree/feat/smaller-release-upgrades), not submitted
  - A quality profile option, off by default, under Upgrades Allowed.
  - Lets an album switch to a release with fewer tracks when no incoming file is worse than any file on disk and at least one is better, such as a FLAC single replacing a 3-track MP3 single. The old files go to the recycle bin.
  - Migration 901.
- **Releases imported into a different album** — branch [`fix/release-imported-to-different-album`](https://github.com/chodeus/Lidarr/tree/fix/release-imported-to-different-album), not submitted
  - A release isn't grabbed again for an album when its last grab for that album, within 14 days, was imported only into other albums, such as an album grabbed for the single of the same name. Stops the same wrong release being grabbed every day.
- **Album artist credits** — branch [`feat/album-artist-credits`](https://github.com/chodeus/Lidarr/tree/feat/album-artist-credits), not submitted
  - A scheduled task reads each album's full artist credit from MusicBrainz, which Lidarr's metadata leaves out, so versions of a single that differ only by guest can be told apart.
  - `{Album Guests}` naming token, such as `{Album Title}{ (Album Guests)}`, and a Media Management option to write the full credit to the artist tags. Migration 902.
- **Removing unmatched queue items** — branch [`fix/ignore-unmatched-download`](https://github.com/chodeus/Lidarr/tree/fix/ignore-unmatched-download), not submitted
  - Removing a queue item that never matched an artist (such as "found multiple artists") without blocklisting logs a warning again, instead of failing with an HTTP 500.
- **Fail downloads with unsafe files** — branch [`feat/fail-downloads`](https://github.com/chodeus/Lidarr/tree/feat/fail-downloads), not submitted
  - Each indexer gets an advanced Fail Downloads option (Executables, Potentially Dangerous). A download whose folder holds only such files is marked failed, so Lidarr blocklists it and searches again, instead of warning.

## How the build works

- Each change is its own branch off `develop` in [chodeus/Lidarr](https://github.com/chodeus/Lidarr), so it can be opened as an upstream PR as it is.
- `meta.json` lists them in `pr_branches`. The hourly `update` workflow records each branch's head in `pr_shas`, and any change to a branch, to upstream `develop` or to the base image triggers a build.
- `build.sh source` merges the branches into `develop` in the listed order. If one no longer merges, the build fails and names it; rebase that branch onto `develop`.
- To add or drop a change, edit `pr_branches`. Drop a change once upstream has merged it.
- Build-only migrations are numbered from 900 so upstream's own numbers can't collide. Before a change goes upstream, give its migration the next upstream number and correct the live database's `VersionInfo`.
