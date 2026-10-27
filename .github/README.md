# Add metadata source profiles

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`. Builds on [`fix/profile-order-and-tags`](https://github.com/chodeus/Lidarr/tree/fix/profile-order-and-tags). Reviewed in [#32](https://github.com/chodeus/Lidarr/pull/32), [#40](https://github.com/chodeus/Lidarr/pull/40), [#41](https://github.com/chodeus/Lidarr/pull/41), [#50](https://github.com/chodeus/Lidarr/pull/50) and [#65](https://github.com/chodeus/Lidarr/pull/65).

- Metadata sources are providers, in a new Metadata Sources section on Settings → Metadata. Built in: Lidarr's own server, and any server that answers like it (URL setting, with Test). Plugins can add sources by implementing `IMetadataSource`.
- Metadata source profiles (Settings → Profiles) choose the sources per artist by tag, like tagging profiles. Each source is Primary or Fallback. Primaries combine with Or (the first that has the artist) or And (every primary; albums only some of them have are added, and an album a source lists by an ID another source keeps as merged counts once, also when the merges chain across sources or one source lists the album twice). The default profile has no tags.
- Lookups go through the artist's profile. A source that throws anything but not-found counts as down; not-found is reported only when every source said so, otherwise the outage is rethrown. Fallbacks are asked when the primaries are down or don't have the artist. With And, a primary that is down fails the lookup before any fallback is asked, so a refresh never sees a partial album list. An album answer without releases counts as not having the album, so the next source is asked; if no source has releases the first answer is used, and an outage still wins.
- Only sources whose ID kind matches the ID are asked (the built-in sources use MusicBrainz IDs). The primaries in a profile must share one ID kind. An ID no source in the profile can handle goes to Lidarr's server if it is a MusicBrainz ID; otherwise the lookup fails as unavailable.
- Search uses the default profile. Each result carries the name of the source that returned it, shown as a label on the add-new page.
- Migration 909 adds `MetadataSources` and `MetadataSourceProfiles`, seeded with Lidarr's server and a default profile that has it as the only primary, so lookups are unchanged until a profile is edited.
- The five lookup interfaces are registered to the profile-aware lookup after plugins load, so a plugin's own lookup can't bypass the profiles.
- Deleting a source removes it from every profile.
- A tag a profile uses counts as in use: the Tags screen lists the profile, the tag cannot be deleted, and the unused-tag cleanup keeps it. `TagDetails.MetadataSourceProfileIds` starts empty, so a `TagDetails` built by a plugin that sets only develop's lists doesn't make `InUse` throw.
- Profile order (add, drag, delete) uses the shared `ProfileOrder` from #12, like delay and tagging profiles; the default profile stays last.
- Each custom server keeps its own changed-albums cache, so one server's list never answers for another. Scheduled refresh also counts Lidarr's server's changes whenever Lidarr's own source isn't enabled, since MusicBrainz lookups can still fall back to it.
- A source that fails is logged, and a failed settings test reported, with its name and what failed, never the error's message, which can hold the request URL: the HTTP status (and whether a web page came back instead of API data), the connection error, or else the error's type. A token in a custom server's path is still in SkyHook's own warning about the request, and in the error a refresh logs when the lookup fails; the log cleanser hides it when it is at least 20 characters and right before `/api/v` (`fix/cleanse-metadata-url-tokens`).
- `SearchController` keeps its 3-argument constructor.

Tests: lookup routing, Or and And, fallback, outage versus not-found, album answers without releases, search fallback and labels, changed-ID lists, profile matching by tag and order, the seeded default, source settings, and the container wiring (profiles win over a plugin's lookup, no other lookup stays registered, the search controller gets the labels), a `TagDetails` with only develop's lists set not counting as in use, And with a down primary and an answering fallback, an album known by an old ID, directly, through a chain of merges across three sources, or twice in one source's list, Lidarr's server in the changed list, a failed source and settings test without the URL, and a web page answer, a connection failure, an unknown error and an HTTP error without a response described without it. Each rule was broken on purpose and caught by a test.

Checked: Core and Host suites on this branch, with only the known AudioTagService, ImportListService and UpgradeMediaFileService failures; the settings screens (add, test, save, delete), profile editing and search labels in a local run.

## Summary by CodeRabbit

* **New Features**
  * Added configurable metadata sources, including support for custom server URLs, connection testing, and enable/disable controls.
  * Added metadata source profiles with tags, primary or fallback sources, configurable search behavior, and drag-and-drop reordering.
  * Search results now show the metadata source when that information is available.
