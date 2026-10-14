# Keep other metadata sources' IDs out of MusicBrainz tags, matching, links and searches

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`. Builds on [`feat/metadata-source-profiles`](https://github.com/chodeus/Lidarr/tree/feat/metadata-source-profiles). Reviewed in [#33](https://github.com/chodeus/Lidarr/pull/33).

Groundwork for metadata sources whose IDs are not MusicBrainz IDs (Deezer and Discogs come next). Lidarr treated every foreign ID as a MusicBrainz ID:

- File tags: a MusicBrainz tag gets the source's ID only when it is a MusicBrainz ID. Otherwise the file keeps the MusicBrainz ID it already has, for both writing and the retag preview.
- Import matching: the recording ID, fingerprint and release ID checks count only against MusicBrainz candidates, since tags and AcoustID carry MusicBrainz IDs only. A `MetadataSourceUnavailableException` during a remote search is skipped like a SkyHook error, and that exception now answers 503.
- Search: an ID search (`mbid:`, `lidarr:`, `lidarrid:`, or a source's own `deezer:…`) goes only to sources of that ID family, never to the others as text.
- `ArtistRepository.FindById` matches old IDs quoted, as `ReleaseRepository` already does, so a short ID can't match inside a longer one (the JSON column is searched with LIKE).
- Links: artist and album pages and search results link to MusicBrainz only for MusicBrainz IDs; search results otherwise use the source's first link. Discord follows the same rule, and Kodi `.nfo` files carry MusicBrainz IDs only.
- Unchanged: the API's `mbId` artist lookup, the webhook and custom script `MBId` fields, and the import list exclusion GUID check.

Tests: tag writing keeps the file's IDs for another source and writes a MusicBrainz source's IDs (MP3 and FLAC); track, fingerprint and release distances count only against MusicBrainz candidates; an unavailable source is skipped in remote candidates; ID searches route by family; the quoted old ID lookup; Discord and Kodi links. Each rule was broken on purpose and caught by a test. `TrackMappingFixture` tracks now carry MusicBrainz recording IDs, like real data.

Checked: Core and Host suites, with only the known AudioTagService, ImportListService and UpgradeMediaFileService failures; frontend type check and lint.

## Summary by CodeRabbit

* **Bug Fixes**
  * Album and artist links in details, search results, and Discord notifications now use MusicBrainz when the ID is valid, or an available link from another source. Links are omitted when no valid destination is available.
  * Audio tags preserve existing MusicBrainz IDs when a metadata source provides a non-MusicBrainz ID; NFO files include only valid MusicBrainz IDs.
  * ID-based searches now route to the relevant metadata source family. Track matching ignores non-MusicBrainz IDs, and matching can continue when remote album search is unavailable.
