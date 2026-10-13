# New: Allow Smaller Release Upgrades option for quality profiles

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`. Reviewed in [#8](https://github.com/chodeus/Lidarr/pull/8).

Lidarr rejects an import whose release has fewer tracks than the album's current release, even when every file is a quality upgrade. The grab-time check already treats such a release as an upgrade, so it downloads and then fails to import.

This adds a quality profile option, off by default and shown only when upgrades are allowed. With it on, `MoreTracksSpecification` accepts a smaller release if it passes the same `IUpgradableSpecification.IsUpgradable` test that `UpgradeDiskSpecification` runs at grab:
- The worst incoming quality is compared against the existing files' qualities.
- Custom formats are scored from the grab history, since album files don't carry the release title. Without a grab it falls back to the lowest-scoring file.
- Library files that a rescan joined into the import are not counted as incoming.

Migration 901 adds `QualityProfiles.AllowSmallerReleaseUpgrades` (default false).

## Summary by CodeRabbit

* **New Features**
  * Quality profiles now include an “Allow Smaller Release Upgrades” option, which is off by default. When enabled, a release with fewer tracks can replace the monitored release if every incoming file qualifies as an upgrade; files not included in the new release are removed.
  * Upgrade checks compare quality and custom-format scores, and exclude existing library files from the comparison. When available, the download’s most recent grab information is used to assess its custom-format score; otherwise, incoming file information is used.
