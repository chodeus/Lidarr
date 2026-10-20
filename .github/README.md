# Fixed: Match imported files whose artist tag lists several artists or holds a web address

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`. It is stacked on `feat/artist-alias-distance` and merges after it. Reviewed in [#PR_NUMBER](https://github.com/chodeus/Lidarr/pull/PR_NUMBER).

Import identification scores the files' artist tag, the album artist where there is one, as a single name against the release artist. Artist carries the heaviest weight in the album distance. A tag that names the album artist alongside others, such as "Artist A;Artist B" or "Artist A feat. Artist B", took a large artist penalty. A web address that a download site wrote into the album artist tag took the full penalty. Either could leave a correct release short of the import threshold.

- The tag's whole text and each artist it lists, split at `;`, `/`, `,`, `&`, "feat.", "ft." and "featuring", are scored against the artist's name and aliases, and the closest pair counts. Keeping the whole text means a name that contains a separator, like "Artist & Band", still matches itself.
- An artist tag that is a web address (a scheme such as `https://`, a `www.` prefix, or a name ending in `.com`, `.net`, `.org`, `.info`, `.biz`, `.ru` or `.io`) isn't scored, as for Various Artists releases. Names with dots that end otherwise, like "d.j.name", are scored as before.
- `Distance.AddStringToClosest` from `feat/artist-alias-distance` now takes several values as well as several targets.

Tests: tags listing the artist with each separator match; a list that names neither artist, an empty tag and dotted names are still penalised; names that contain a separator match themselves; web addresses, with or without padding, aren't scored. Six mutations (whole tag only, split names only, no web-address check, a broader web-address match, the farthest of the tag's names, no trimming) each fail a test, and the alias tests still pass.
