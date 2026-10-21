# Fixed: Custom format upgrades past the quality or custom format cutoff

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`. Reviewed in [#24](https://github.com/chodeus/Lidarr/pull/24).

**Upgrade rules**
- A higher quality is no longer an upgrade once the files meet the quality cutoff, even if the custom format cutoff is unmet. Before, `CutoffSpecification` passed while either cutoff was unmet and `UpgradeDiskSpecification` then took any better quality, so a profile with a high custom format cutoff kept upgrading past its quality cutoff.
- A same-quality upgrade for a better custom format score is refused once the existing score meets the custom format cutoff.
- With upgrades off, the custom format cutoff is the profile's Minimum Custom Format Score, and a better custom format score no longer passes the disk, queue or history checks. Revision upgrades still pass.
- New quality profile option, Minimum Custom Format Score Increment (shown when upgrades are allowed and the profile has custom formats): how much a custom format score must improve to count as an upgrade. The default of 1 keeps today's behaviour, and a very large value can't overflow into an upgrade. Migration 906.
- A Size condition reads the files on disk by the album's total size, as it reads a release. Before, each file was read by its own size, so an album-sized condition never matched the files and the same release was grabbed again on every RSS sync; develop does the same. Sonarr reads each episode file by its own size, but a Sonarr release is usually one episode, so its two sides already match.

**Shape**
- `UpgradableSpecification.GetUpgradeRejectReason` says why a release is not an upgrade, and the disk, queue and history checks (which inject the concrete class) reject with a message per reason. `IUpgradableSpecification` keeps develop's five members, pinned by a reflection test of the member names; the interface's text is develop's, so plugins that implement it still load. `ICustomFormatCalculationService.ParseCustomFormats`, which gives each file the album's size, has a default body.
- An album has several files. Each file is judged on its own: a downgrade for any file rejects; otherwise one upgraded file is enough, as before. A revision counts only at the same quality, as in develop, so a proper at a lower quality never blocks a better quality, and a higher-quality proper is not an upgrade once the cutoff is met or with upgrades off.
- Each existing file is judged by its own custom formats; before, every file was scored with the first file's. A file at the same quality that would lose custom format score counts as a downgrade, like a lower quality does. The cutoff counts as met only when every file meets it.
- `CutoffSpecification` keeps develop's public shape. With upgrades off, its message names the first allowed quality as the cutoff.
- The API resource defaults `minUpgradeFormatScore` to 1, so clients that don't send the field can still save profiles.

`feat/smaller-release-upgrades` uses `IsUpgradable` to match the grab check at import, so it follows the same rules. Its test profile had no cutoff, which this check treats as met; it now sets real cutoffs (2b00d24), and its tests pass merged with this branch.

Tests: 25 new cases across `UpgradeSpecificationFixture`, `UpgradeDiskSpecificationFixture` and `CutoffSpecificationDecisionFixture`, and `AlbumSizeCustomFormatFixture`, which runs the real custom format calculation for the disk and cutoff checks; each rule fails at least one of them when removed. The migration and the housekeeper change have database tests, which pass.

## Summary by CodeRabbit

* **New Features**
  * Quality profiles now include a minimum custom format score increase required before a release is considered an upgrade. Set the threshold in the profile when upgrades and custom formats are enabled.
  * Upgrade decisions now provide more specific reasons, including when a release misses a quality or custom format cutoff, does not improve the score enough, or upgrades are disabled.
* **Bug Fixes**
  * Upgrade and cutoff decisions now account for each existing file, avoiding acceptance based only on the best-scoring file.
