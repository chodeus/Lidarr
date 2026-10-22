# New: Notifications when manual interaction is required for importing

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`. Reviewed in [#15](https://github.com/chodeus/Lidarr/pull/15).

Ported from Sonarr's "On Manual Interaction Required" notification (5ec2827 and the event-type fix f56d504), in Sonarr's names and structure: `ManualInteractionRequiredEvent`, `ManualInteractionRequiredMessage`, `TrackedDownload.HasNotifiedManualInteractionRequired`, the `INotification`/`NotificationBase`/`NotificationDefinition`/`NotificationFactory` members, the API resource fields, the webhook payload and `WebhookDownloadClientItem`, and the connection checkbox.

- `CompletedDownloadService` publishes the event when a download becomes import blocked (unparsed, no artist match), once per download and reason while Lidarr runs: a download that keeps cycling through Import Pending stays quiet, and one blocked again for a new reason, such as after the user added the artist and the title still maps no album, notifies again. The flag is in memory, as in Sonarr, so a download still blocked notifies again after a restart. A download whose whole folder is rejected (an executable, an archive to extract) stays pending and retries, as in Sonarr, so it doesn't notify; with `feat/fail-downloads` in the image, a rejection its indexer doesn't fail is import blocked and notifies. Lidarr's existing "On Import Failure" already covers import failed, so this doesn't fire there.
- All 21 providers Sonarr covers implement it. The simple ones mirror each provider's own `OnDownloadFailure`. Discord gets `ManualInteractionFields`; the webhook sends `ManualInteractionRequired`.
- Migration 904 adds `Notifications.OnManualInteractionRequired` (900+ is this build's range).

Changes from Sonarr: the download remembers the reason it was notified for (`TrackedDownload.ManualInteractionRequiredReason`, carried over with the flag), so a new reason notifies again; Sonarr notifies once per download. For downloads that never mapped to an artist:
- The message falls back to the download title when artist, albums or quality are missing.
- Such a download reaches only notifications without tags. Sonarr's tag check dereferences the missing series, and the caught exception marks the notification failing.
- Discord, CustomScript and the webhook payload skip the artist parts when there is no artist.
- Discord links to MusicBrainz, in the embed and the Links field, only when the artist's id is a MusicBrainz id, so an artist from another metadata source gets no broken link.
- The webhook's download info carries the tracked download's indexer. Sonarr declares the field but never sets it.

Tests: the event fires once per reason, again for a new reason and not while a download cycles through Import Pending (re-audit, 2026-10), the flag and reason survive re-tracking, the handler's matched, missing-quality, untagged, tagged-unmatched and tagged-matched cases, the webhook payload with no artist, and the `Supports` flags. Nine mutations, all building, each fail a test.

## Summary by CodeRabbit

* **New Features**
  * Notifications can alert you when a download requires manual attention. Enable this event in notification settings; supported services can include download and album details.
  * Discord alerts offer configurable fields, and webhooks include download information. Other supported notification services can also deliver these alerts.
  * Alerts use album and quality details when available, or fall back to the download title. Artist tags determine which notifications receive alerts when an artist is matched.
* **Bug Fixes**
  * Avoided repeat alerts when a download remains blocked for the same reason, while allowing a new alert if the reason changes. Notification status is preserved when a download is tracked again.
