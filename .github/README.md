# New: Album artist credits from MusicBrainz

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`. Reviewed in [#2](https://github.com/chodeus/Lidarr/pull/2).

Lidarr keeps one artist per album and per track, and the metadata server returns only the primary artist. Versions of a single that differ only by guest ("Song" by Artist feat. A and "Song" by Artist, B & A) look identical to Lidarr, get the same folder name, and the second import fails with "Destination already exists".

**What it adds**
- `AlbumArtistCredits` table (migration 902, custom-build range): album id, joined credit, guests, the credit's artists (name, MBID, join phrase), last fetched.
- `RefreshAlbumArtistCreditsCommand`, a scheduled task every 15 minutes, up to 700 albums per run. It reads `ws/2/release-group/{id}?inc=artist-credits` at one request per 1.1 s.
  - Order: albums with no credit that share a title with a sibling, then other albums with no credit (newest first), then credits older than 90 days.
  - A 404 stores an empty credit. Any other failure stores nothing and is retried. Five failures in a row end the run.
- `{Album Guests}` naming token: the credit without the album's artist, e.g. `{Album Title}{ (Album Guests)}`.
- "Write Full Artist Credits" option (Media Management, off by default). It writes the credit to the album artist tag, and to the artist tag of tracks by the album's own artist; tracks by other artists keep their artist.
- `GET /api/v1/album/artistcredit?albumIds=…`

**Compatibility with the rest of the build.** `AudioTagService` and `FileNameBuilder` read credits through `AlbumArtistCreditLookup`, a static lookup set at startup. Their constructors and fields are untouched: `tagging-profiles` replaces `AudioTagService`'s constructor, and plugins are built against develop's. The UI setting sits in Media Management because `tagging-profiles` removes the Metadata page's provider section. The en.json keys sort where no other branch edits.

**Verified**
- 29 new tests. Eight mutations each fail a test (checked that each one built).
- Branch alone: full Core suite 3252 passed, 1 failed (`no_update_when_version_higher`, which also fails on develop).
- The six `pr_branches` plus this one merge cleanly in image order onto develop `da7b4df`. On that merge, the full Core suite passes 3338 with 1 failure; without this branch it passes 3309 with the same single failure (+29 tests, no new failures). Migrations are unique: 082, 900, 901, 902.
- Smoke boot on a fresh DB:
  - Migration 902 applies, and the Sleezer 1.24.7 plugin loads with no `MissingMethodException`.
  - The endpoint returns 200, and the task and option are present.
  - Adding an artist whose single has three guest versions and running the task stored 37 of 37 credits in 43 s. The three versions came back as distinct guests; a second run is a no-op.
- `eslint --config frontend/.eslintrc.js` passes on the two frontend files.

## Summary by CodeRabbit

* **New Features**
  * Added an advanced setting to write full album artist credits to album and track artist tags.
  * Added the `{Album Guests}` naming token, which includes guest artists in album filenames and is omitted when no guests are available.
  * Album artist credits are fetched in the background and made available through the API.
