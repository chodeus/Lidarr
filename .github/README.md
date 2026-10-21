# New: Fail downloads containing executable or potentially dangerous files per indexer

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`. Reviewed in [#14](https://github.com/chodeus/Lidarr/pull/14).

Ported from Sonarr's per-indexer Fail Downloads option (`FailDownloads`, `CachedIndexerSettingsProvider`, `RejectedImportService`, `TrackedDownload.Fail`, and the potentially dangerous extension list), in Sonarr's names and structure.

- Each of Lidarr's indexers gets an advanced **Fail Downloads** multi-select: Executables, Potentially Dangerous, User Defined Extensions (the extensions rejected in Media Management).
- The folder check in `DownloadedTracksImportService` now returns a typed `ImportRejection` (`ExecutableFile` or `DangerousFile`), and also rejects Sonarr's potentially dangerous extensions (.arj, .lnk, .lzh, .ps1, .scr, .vbs, .zipx).
- When a download's indexer fails that file type, `RejectedImportService` marks it failed (`DownloadFailedPending`), so the existing failed-download handling blocklists it and searches again. Otherwise it warns, as before, and when tracks were rejected too their rejections decide, so the import still ends as Import Failed with each track's reason, and the unsafe file is added as a caution. A download that is a single unsafe file is checked the same way. The grab-time check reads a torrent's file list; a torrent added from a magnet link, or whose file list can't be read, isn't checked, as its help text now says.

Differences from Sonarr, both for the custom build:
- `IIndexerSettings.FailDownloads` has a default implementation (empty). Plugin indexer settings compiled against develop implement `IIndexerSettings` without this member, and would otherwise fail to load.
- Lidarr rejections are plain `Rejection`s with a text reason, so `ImportRejection` subclasses it with `ImportRejectionReason` rather than Sonarr's generic `Rejection<T>`. `CompletedDownloadService` calls `RejectedImportService` only for a folder-level rejection, the branch that already warned; a single rejected track keeps Lidarr's import-failed handling.

Tests: `RejectedImportServiceFixture` (each option and reason, missing settings, no release, not rejected), `CachedIndexerSettingsProviderFixture` (including settings without the property), the typed reasons in `DownloadedTracksImportServiceFixture`, and both outcomes of the `CompletedDownloadService` hook. `FailDownloadsImportFixture` (re-audit, 2026-10) runs the real import, rejection and indexer settings services: a single-file executable download is failed, a dangerous file beside rejected tracks fails the download when the indexer fails it, and otherwise the import stays Import Failed with the caution added. Leaving out the single-file check, the track fall-through, the caution, or the stop once the download is failed each fails a test. Eight mutations, all building, each fail a test.

## Summary by CodeRabbit

* **New Features**
  * Configure supported indexers to fail downloads containing executable, potentially dangerous, or user-rejected file extensions.
  * Block configured unsafe torrent files before grabbing, with fallback handling for unavailable file lists.
  * Set custom rejected file extensions in Media Management settings.
  * Risky files are identified during import; affected downloads are failed and removable or remain pending with a warning based on indexer settings, and when tracks are rejected too the import ends as Import Failed with each track's reason.
* **Bug Fixes**
  * Import warnings now distinguish executable, potentially dangerous, and user-rejected files.
  * Failed imports are processed in the same pass as completed-download handling.
