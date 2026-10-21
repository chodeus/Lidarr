# Add Discogs as a built-in metadata source

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`. Builds on [`feat/deezer-metadata-source`](https://github.com/chodeus/Lidarr/tree/feat/deezer-metadata-source). Reviewed in [#35](https://github.com/chodeus/Lidarr/pull/35).

- Discogs as a built-in metadata source, written from Discogs's public API. IDs are `discogs:artist:<n>`, `discogs:master:<n>` and `discogs:release:<n>`; Discogs has no track IDs, so tracks use the release ID and track index.
- Settings: an optional personal access token (sent in a header, never the URL) and Pressings Per Album (default 25, 0 = every pressing). A master album loads its main release, then the pressings most people own. The settings test marks the Token field only when Discogs rejects the token.
- Artist info keeps the artist's own releases (role Main) that are audio and allowed by the metadata profile. A master's type comes from its main release. Promos, samplers and test pressings map to Promotion, unofficial releases to Bootleg; video and data formats are dropped, but DVD-Audio and Blu-ray Audio count as audio. The artist list joins an entry's formats with " + " and shortens names (`Comp`, `TP`, `W/Lbl`, `Maxi`, `DVD-V`); both are read.
- Discogs lists an artist's own releases before every other credit, so the list is read only up to the first other role (Radiohead: 3 of 33 pages). If own releases appear after another role on the same page, or page 1 starts with another role, the whole list is read instead. An own release after another role on a later page is not seen, since only the rest of the list would show it; the album is then left out of the artist's list, and a refresh checks it against Discogs before removing anything (`fix/keep-unlisted-metadata`).
- Bootlegs (role UnofficialRelease) come after every other credit, so they are read from the last page back, and only when the metadata profile allows Bootleg (Radiohead: 630 bootlegs in 8 requests). A bootleg on page 1, before the run of bootlegs at the end, also counts as a broken order, so the whole list is read. Every bootleg credit counts as Bootleg, since the artist list writes the format many ways ("Unofficial", "P/Unofficial", "CD + DVD-V + Unofficial"). "Unofficial" also beats "Promo" when a release has both.
- Lists are never returned partly: a missing page fails the lookup, and so does a list past 100 pages (an artist's own releases past 100 pages fail after one look at page 101, so Bach costs 2 requests, not 100). A pressing Discogs can't find fails the album lookup too.
- Index entries, which hold a classical work's movements, expand into their tracks, named `Work: Movement`. The monitored pressing is picked from those with tracks.
- Track positions: vinyl sides pair up on a disc (A and B are disc 1, in either case), CD positions read disc-track. Durations, unknown date parts and Discogs's `(2)` name numbers are handled.
- Rate limit: requests are spaced for 60 a minute with a token and 25 without. When `X-Discogs-Ratelimit-Remaining` reaches 0, the next request waits out the 60-second window; a 429 waits for `Retry-After` (or 60 seconds) and retries once. A not-found is null; anything else counts as Discogs being down, an empty reply included.

Tests: the reply shapes through the real client (an empty or null reply for an artist or master, a bootleg on page 1 before the run at the end, lower-case vinyl sides, index tracks from JSON); the API client (not-found, errors, failed requests, token header and rate limits, paging, missing pages, the 100-page limit, stopping at the end of own releases and falling back to a full read, reading bootlegs from the end, the spent-limit pause, one retry), ID parsing, format and status classification (multi-format and shortened entries, DVD-Audio), positions, durations, dates, release mapping (index tracks), missing pressings, the monitored pressing, the token field, artist filtering, pressing order and limit, standalone releases, and search. Each rule was broken on purpose and caught by a test. Checked live against Discogs without a token.

## Summary by CodeRabbit

* **New Features**
  * Added Discogs as a metadata source for searching artists and albums and retrieving artist, album, release, and track details.
  * Discogs results respect metadata profile settings, including audio formats and bootleg preferences.
  * Added an optional API token and a configurable limit for album pressings; set the limit to zero to load all pressings.
  * Search results can reuse artists and albums already in your library.
* **Bug Fixes**
  * Discogs requests now handle rate limits with pauses and retries, and report unavailable or incomplete results clearly.
