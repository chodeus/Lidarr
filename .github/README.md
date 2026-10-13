# Hide metadata server tokens in URL paths from logs

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`. Reviewed in [#29](https://github.com/chodeus/Lidarr/pull/29).

- Log lines mask a token-like path segment (20+ characters) that sits right before `/api/vN`, as in `https://host/t/<token>/api/v0.4/artist/...`
- Hostnames, IDs later in the path and short URL bases are kept

Tests: two lines that must be masked, and one line that keeps a long hostname, an ID after `/artist/` and a short URL base. Removing the rule fails all three; removing the hostname lookbehind fails the third.

## Summary by CodeRabbit

* **Bug Fixes**
  * Sensitive metadata-server tokens in URL paths are now redacted from logs, including request and search URLs. Shorter hostname and path details remain visible.
