# New: Allow Smaller Release Upgrades option for quality profiles

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`. Reviewed in [#8](https://github.com/chodeus/Lidarr/pull/8).

Lidarr rejects an import whose release has fewer tracks than the album's current release, even when every file is a quality upgrade. The grab-time check already treats such a release as an upgrade, so it downloads and then fails to import.

This adds a quality profile option, off by default and shown only when upgrades are allowed. With it on, `MoreTracksSpecification` accepts a smaller release if it passes the same `IUpgradableSpecification.IsUpgradable` test that `UpgradeDiskSpecification` runs at grab:
- The worst incoming quality is compared against the existing files' qualities.
- Custom formats are scored the same way on both sides. When the download has a grab and every file on disk traces to one (the latest import of its track, if that import brought the file), the grab titles are scored, and the best one on disk is compared. Otherwise both sides are scored by file names: the worst new file against the best file on disk. Album files don't carry the release title, and Lidarr never sets `TrackFile.SceneName`, so scoring a grab against file names would let a format seen only in the grab's title (a release group, say) fake an upgrade.
- An album set to one specific release is not switched: `ReleaseWantedSpecification` rejects any other release unless the album accepts any release.
- Library files that a rescan joined into the import are not counted as incoming.

Migration 901 adds `QualityProfiles.AllowSmallerReleaseUpgrades` (default false). Develop's `MoreTracksSpecification(Logger)` constructor is kept for plugins built against it.

- `MoreTracksSpecificationFixture`: grab against grab, a grab against file names, the best grab on disk, a file without a grab, the latest import, a file a rescan added later.
- `MoreTracksSpecificationRealFormatsFixture`: the real format calculation and title specifications.
- `MoreTracksSpecificationContainerFixture`: the container builds the specification with its full constructor.

## Summary by CodeRabbit

* **New Features**
  * Quality profiles now include an “Allow Smaller Release Upgrades” option, off by default and available only when upgrades are allowed. When enabled, a release with fewer tracks can replace the current release if it qualifies as an upgrade based on quality and custom-format scores.
  * Albums configured for a specific release are not switched. Files not included in the replacement release are removed.
