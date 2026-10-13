# New: Notifications when manual interaction is required for importing

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`. Reviewed in [#15](https://github.com/chodeus/Lidarr/pull/15).

Ported from Sonarr's "On Manual Interaction Required" notification (5ec2827 and the event-type fix f56d504), in Sonarr's names and structure: `ManualInteractionRequiredEvent`, `ManualInteractionRequiredMessage`, `TrackedDownload.HasNotifiedManualInteractionRequired`, the `INotification`/`NotificationBase`/`NotificationDefinition`/`NotificationFactory` members, the API resource fields, the webhook payload and `WebhookDownloadClientItem`, and the connection checkbox.

- `CompletedDownloadService` publishes the event once per download when it becomes import blocked (unparsed, no artist match). A download whose whole folder is rejected (an executable, an archive to extract) stays pending and retries, as in Sonarr, so it doesn't notify. Lidarr's existing "On Import Failure" already covers import failed, so this doesn't fire there.
- All 21 providers Sonarr covers implement it. The simple ones mirror each provider's own `OnDownloadFailure`. Discord gets `ManualInteractionFields`; the webhook sends `ManualInteractionRequired`.
- Migration 904 adds `Notifications.OnManualInteractionRequired` (900+ is this build's range).

Changes from Sonarr, for downloads that never mapped to an artist:
- The message falls back to the download title when artist, albums or quality are missing.
- Such a download reaches only notifications without tags. Sonarr's tag check dereferences the missing series, and the caught exception marks the notification failing.
- Discord, CustomScript and the webhook payload skip the artist parts when there is no artist.

Tests: the event fires once per download, the flag survives re-tracking, the handler's matched, missing-quality, untagged, tagged-unmatched and tagged-matched cases, the webhook payload with no artist, and the `Supports` flags. Nine mutations, all building, each fail a test.

## Summary by CodeRabbit

* **New Features**
  * Notifications can alert you when a download needs manual attention. Enable this event in notification settings; supported services can include download and album details.
  * Discord alerts offer configurable fields, and webhooks include download details for manual-interaction events. Other supported notification services can also deliver these alerts.
* **Bug Fixes**
  * Prevented duplicate manual-interaction alerts when an import remains blocked or a download is tracked again.
