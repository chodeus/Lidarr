# Fixed: A manual import tied to a download that never completed no longer fails after importing

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`. Reviewed in [#PR_NUMBER](https://github.com/chodeus/Lidarr/pull/PR_NUMBER).

After a manual import, `ManualImportService.Execute` cleans up each download's folder and marks the download imported. It reads the folder from the tracked download's import item, which only completed-download handling sets. A manual import sent with the download id of an item that handling never processed (one its client still reports as downloading, or one whose import failed) imported its files, then threw a `NullReferenceException`. The command reported "failed", the folder stayed, and the download was never marked imported.

- When the import item is unset, `Execute` asks for it as `GetMediaFiles` already does (`IProvideImportItemService.ProvideImportItem`), so an import sent straight to the API behaves like one opened from the manual import dialog.
- An import item that completed-download handling did set is left alone.

Tests: `ManualImportExecuteFixture` runs `Execute` with a download that has no import item: the files import, the folder is cleaned up and the download is marked imported. The same test throws `NullReferenceException` on `develop`. A second test checks an import item that was already set isn't replaced.
