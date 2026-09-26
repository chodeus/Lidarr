# Lidarr custom build

`ghcr.io/chodeus/lidarr:custom` is Lidarr's `develop` branch with the changes below merged in, on the hotio base image, with the [Sleezer](https://github.com/chodeus/sleezer) plugin preinstalled.

## Bundled plugin

- The image ships the latest [Sleezer](https://github.com/chodeus/sleezer) release. Nothing to install from System → Plugins.
- Each container start copies the plugin's files into `/config/plugins/chodeus/sleezer`, where a System → Plugins install would put them. Sleezer's saved settings (such as Tidal tokens) and any other files there are kept.
- The image decides the version. An update or uninstall from System → Plugins lasts only until the next restart; pull a newer image to update.

## What it adds to Lidarr develop

- **Tagging profiles** — branch [`tagging-profiles`](https://github.com/chodeus/Lidarr/tree/tagging-profiles), upstream PR [#5785](https://github.com/Lidarr/Lidarr/pull/5785)
  - The Write Metadata to Audio Files settings become tagging profiles under Settings → Profiles, applied to artists by tag.
  - Each profile can skip hardlinked files (on by default), so a seeding torrent's files are never retagged.
  - Existing settings migrate into a default profile. Migration 082.
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

## How the build works

- Each change is its own branch off `develop` in [chodeus/Lidarr](https://github.com/chodeus/Lidarr), so it can be opened as an upstream PR as it is.
- `meta.json` lists them in `pr_branches`. The hourly `update` workflow records each branch's head in `pr_shas`, and any change to a branch, to upstream `develop` or to the base image triggers a build.
- The same workflow records Sleezer's latest release in `sleezer_version` and its zip's SHA-256 in `sleezer_zip_sha256`; the image build checks the download against it. A release whose zip isn't uploaded yet is picked up the following hour.
- The build's smoke test fails unless Lidarr reports that exact Sleezer version as loaded.
- `build.sh source` merges the branches into `develop` in the listed order. If one no longer merges, the build fails and names it; rebase that branch onto `develop`.
- To add or drop a change, edit `pr_branches`. Drop a change once upstream has merged it.
- Build-only migrations are numbered from 900 so upstream's own numbers can't collide. Before a change goes upstream, give its migration the next upstream number and correct the live database's `VersionInfo`.
