# Fixed: Bracketed featured credit removing the rest of a track title

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`. Reviewed in [#21](https://github.com/chodeus/Lidarr/pull/21).

`CleanTrackTitle` removes a featured-artist credit before track titles are compared, but its pattern runs to the end of the title. Stores put the credit before the version, so "Run It Back (feat. Caroline Byrne) (Luuk Van Dijk Remix)" was compared as "Run It Back": the remix could not match its own track, and could match the original instead.

A bracketed credit (`(feat. …)`, `[ft. …]`, `(featuring …)`, `(produced by …)`) is now removed up to its own closing bracket. A bare credit (`Song feat. Someone`) and an unclosed bracket keep the old behaviour, and `CleanAlbumTitle` is unchanged.

Tests: three bracketed credits followed by a version, and a bare credit. With the old pattern the three bracketed cases fail and the bare one passes.

## Summary by CodeRabbit

* **Bug Fixes**
  * Track title parsing handles bracketed featuring and producer credits more consistently. Remix and mix labels that follow those credits are retained when they’re separate bracketed sections, while trailing remix labels attached to unbracketed feature text are removed with that text.
