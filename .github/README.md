# Add the artist's tags to new qBittorrent torrents

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`. Reviewed in [#46](https://github.com/chodeus/Lidarr/pull/46).

- New qBittorrent option, Add Artist Tags (off by default): after Lidarr adds a torrent, the artist's Lidarr tags are added to it as qBittorrent tags.
- Only qBittorrent's own labels change. The torrent, its files and seeding are untouched, and torrents already in qBittorrent are left alone.
- A failure to add the tags is logged as a warning and the grab still succeeds. qBittorrent's v1 API has no tags.
- Develop's `QBittorrent` constructor is kept, and the new `IQBittorrentProxy.AddTags` has a default body.

Tests: tags added for a torrent file and a magnet link, none when the option is off or the artist has no tags, the grab surviving a tag failure, and the kept surfaces. Each rule was broken on purpose and caught by a test.

## Summary by CodeRabbit

* **New Features**
  * Added an option to apply an artist’s tags to new qBittorrent downloads, including magnet links. Tags are added without changing the torrent or its files.
  * If tag support is unavailable or adding tags fails, the download can still complete.
