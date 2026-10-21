# Fixed: Bracketed featured credit removing the rest of a track title

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`. Reviewed in [#21](https://github.com/chodeus/Lidarr/pull/21).

`CleanTrackTitle` removes a featured-artist credit before track titles are compared, but its pattern runs to the end of the title. Stores put the credit before the version, so "Run It Back (feat. Caroline Byrne) (Luuk Van Dijk Remix)" was compared as "Run It Back": the remix could not match its own track, and could match the original instead.

A bracketed credit (`(feat. …)`, `[ft. …]`, `(featuring …)`, `(produced by …)`) is now removed up to its own closing bracket, including a credit with one level of brackets inside it (`(feat. A (B))`). A bare credit (`Song feat. Someone`) and an unclosed bracket keep the old behaviour, and `CleanAlbumTitle` is unchanged.

A track title's `(Original Mix)` or `[Original Mix]` is removed too. Stores label the default version that way and MusicBrainz leaves it out of the title; the old pattern removed it only by accident, when a credit came before it, and keeping it now would have scored those tracks worse against their own title.

Tests: three bracketed credits followed by a version, and a bare credit. With the old pattern the three bracketed cases fail and the bare one passes. Re-audit (2026-10): an `(Original Mix)` after a credit and on its own, and a credit with brackets inside it, alone and before a remix; leaving out the nesting or the `Original Mix` pattern fails them.

## Summary by CodeRabbit

* **Bug Fixes**
  * Track title parsing handles bracketed featuring and producer credits more consistently. Remix and mix labels that follow those credits are retained when they’re separate bracketed sections, while trailing remix labels attached to unbracketed feature text are removed with that text.
