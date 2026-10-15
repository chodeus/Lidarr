# Fall back to another download client

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`. Reviewed in [#44](https://github.com/chodeus/Lidarr/pull/44).

- When a download client can't be reached, the next client of the same protocol is tried: by priority, then within each priority starting after the client that last took a release, even when that client is not listed this time.
- Only a client that was never reached falls back: the connection failed (refused, unknown host, TLS or proxy) or the login was refused. A timeout, a lost response or an error from the client can come after the client took the release, so it is reported as before and no other client is tried. A problem with the release itself (gone, blocklisted, rejected as a duplicate, an indexer error) is not tried on another client either.
- The last client's own error is reported as before, so a setup with one client behaves the same.
- Each attempt waits for the per-host grab limit, since each one fetches the release again.
- An indexer's own download client, or a client picked by hand, is used alone.
- `IProvideDownloadClient` gains `GetDownloadClients(protocol, ...)` and `ReportSuccessfulDownloadClient`, with default bodies for providers built against develop.

Tests: the client order (protocol, tags, indexer client, blocked clients, priority, rotation per protocol, rotation after a client no longer listed), falling back when the connection or login failed, not falling back on a timeout, a lost response, a client error, an unreachable indexer, a release error or after a client took it, the last error reported, a chosen client used alone, and the default bodies. Each rule was broken on purpose and caught by a test.

## Summary by CodeRabbit

* **New Features**
  * Downloads can be routed through eligible clients in priority order, with rotation among clients at the same priority after a successful download.
  * When a client encounters a connection or authentication failure before accepting a release, the next eligible client is tried. Release-specific failures and other non-retryable errors stop the process.
  * Client selection can account for protocol, tags, indexer, and blocked-client status. An explicitly selected client remains the only one used.
* **Bug Fixes**
  * Downloads are not retried after a client accepts the release, even if a later status update fails.
