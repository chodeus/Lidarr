# New: MusicBrainz links in Gotify, Pushcut and Telegram notifications

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`. Builds on [`feat/manual-interaction-notification`](https://github.com/chodeus/Lidarr/tree/feat/manual-interaction-notification). Reviewed in [#16](https://github.com/chodeus/Lidarr/pull/16).

Ported from Sonarr's notification metadata links, in Sonarr's names and structure: `MetadataLinkType`, `NotificationMetadataLink`, `NotificationMetadataLinkGenerator`, and the `MetadataLinks` setting on Telegram (4eab168), Gotify with `PreferredMetadataLink` and the click-through URL (3c85713), and Pushcut actions (6f451b2, v5).

- Lidarr's link types are MusicBrainz Artist and MusicBrainz Album. The album link is the release group, and it is only added when the message covers exactly one album.
- Each provider defaults to no links, so existing notifications don't change until links are picked.
- Health, download-failure and update messages carry no links, as in Sonarr. Import failures link the artist, since Lidarr knows it there.
- Validation matches Sonarr: every selected link must be a known type, and Gotify's preferred link must be one of the selected links.
- Only a canonical MusicBrainz id (the dashed 36-character form, not the empty GUID) gets a link, and a type selected twice links once.
- Gotify switches to markdown once a poster or a link is added, so the message text is escaped then: a name such as "M*A*S*H", or a title shaped like a link, shows as written. Each link gets its own line. Sonarr sends it unescaped.
- Pushcut leaves `actions` out when no links are selected, as develop's payload did.
- Test messages: Telegram's always carries a Lidarr.audio link, Gotify's only when links are selected, and Pushcut's none.

- The Telegram and Pushcut proxies keep develop's `SendNotification(title, message, settings)` overloads for plugins, and the new link overloads default to them.

Tests: the generator (both links, several albums, selection, no artist, no artist ID, padded, empty and undashed ids, a type selected twice), the validators, Telegram anchors, Pushcut actions and no actions without links, and Gotify's links, click URL, markdown content type, escaped names with markdown and unescaped ones without. Twenty-two mutations, all building, each fail a test.

## Summary by CodeRabbit

* **New Features**
  * Telegram and Pushcut notifications can include configurable MusicBrainz artist and album links for relevant events.
  * Gotify notifications can include configured metadata links, with clicks opening the selected preferred link.
  * Test notifications include a link to Lidarr’s website.

* **Bug Fixes**
  * Metadata-link settings are validated, including ensuring Gotify’s preferred link is selected when links are configured.
