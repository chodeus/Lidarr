# Fixed: Custom build never reports to Servarr's Sentry

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`. Custom-only, never submitted upstream.

The custom build is stamped with major version 10, so `RuntimeInfo` treats it as an unofficial build. Lidarr turned Sentry on for every unofficial build whatever the analytics setting (`ReconfigureLogging`: `SentryEnabled = ... || RuntimeInfo.IsDevelopment`), so the image sent its error reports to Servarr's development Sentry project.

- `NzbDroneLogger.Register` adds the Sentry target only for an official build; the three places that look it up already allow it to be missing
- official builds are unchanged

Tests: started the built app on a fresh data folder. Without the change, Sentry's cache folder appears in the data folder; with it, the app starts, answers `/ping`, shuts down, and no `Sentry` folder is created. `Lidarr.Common.Test` passes except `should_be_able_to_start_process`, a process-start timing test that also fails without the change.
