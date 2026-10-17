# New: Allow Smaller Release Upgrades option for quality profiles

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`. Reviewed in [#8](https://github.com/chodeus/Lidarr/pull/8).

Lidarr rejects an import whose release has fewer tracks than the album's current release, even when every file is a quality upgrade. The grab-time check already treats such a release as an upgrade, so it downloads and then fails to import.

This adds a quality profile option, off by default and shown only when upgrades are allowed. With it on, `MoreTracksSpecification` accepts a smaller release if it passes the same `IUpgradableSpecification.IsUpgradable` test that `UpgradeDiskSpecification` runs at grab:
- The worst incoming quality is compared against the existing files' qualities.
- Custom formats are scored the same way on both sides. When the download has a grab and every file on disk traces to one (the latest import of its track), the grab titles are scored, and the best one on disk is compared. Otherwise both sides are scored by file names: the worst new file against the best file on disk. Album files don't carry the release title, and Lidarr never sets `TrackFile.SceneName`, so scoring a grab against file names lets a format seen only in the grab's title (a release group, say) fake an upgrade.
- Library files that a rescan joined into the import are not counted as incoming.
- An album set to one specific release is not switched: `ReleaseWantedSpecification` rejects any other release unless the album accepts any release.

Replacing the album's files on a download import no longer recycles them first. They move into a hidden `.lidarr-replaced` folder beside the album (disk scans skip dot folders), and their rows follow them. Once at least one file of the new release imports, they go to the recycle bin and their rows are deleted, as before. If none imports (destination exists, permissions, any move error), they move back, the rows get their paths back, and the album returns to its previous release. A failure while moving them aside puts back what was moved and fails the import, as a recycle bin failure did.

Migration 901 adds `QualityProfiles.AllowSmallerReleaseUpgrades` (default false).

## Summary by CodeRabbit

* **New Features**
  * Quality profiles now include an “Allow Smaller Release Upgrades” option, which is off by default. When enabled, a release with fewer tracks can replace the monitored release if every incoming file qualifies as an upgrade; files not included in the new release are removed.
  * Upgrade checks compare quality and custom-format scores, and exclude existing library files from the comparison. When available, the download’s most recent grab information is used to assess its custom-format score; otherwise, incoming file information is used.
