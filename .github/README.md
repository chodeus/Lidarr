# New: Import a release's own files, such as its cue sheet and rip log

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`. It is stacked on [`fix/extras-link-track-files`](https://github.com/chodeus/Lidarr/tree/fix/extras-link-track-files) and merges after it.

With Import Extra Files on, Lidarr imports only extra files named after a track, such as `01 - Song.lrc` beside `01 - Song.flac`. A release's own files, such as `Album.cue` or the rip log, never match a track's name, so they stay in the download folder.

- Once an album's tracks are imported, every file in their download folders that has a wanted extension and that no audio file's name starts goes into the folder the album's tracks share (their deepest common folder), under its own name and byte for byte. Every audio file in those folders counts, so the lyrics of a track that didn't import aren't taken for the release's. The row has the album and no track file: migration 915 lets `ExtraFiles.TrackFileId` be null, as `MetadataFiles` already allows.
- A release file is never written over an existing file: one an import that doesn't replace kept, one nothing recorded, or a second download folder's file of the same name. It is skipped with a warning.
- An import that replaces the album's files (every download import does; a manual import when asked to) first sends the previous release's files to the recycle bin. One that doesn't replace keeps them.
- Renaming moves a release file to the folder the album's tracks now share, keeping its name.
- When a user or a disk scan removes the album's last track file, its release files go to the recycle bin. An import that deletes and adds the album's files leaves them to the import, and removing an album without its files leaves them on disk; housekeeping then drops their rows once the album is gone.
- Wanted extensions match regardless of case and only after a dot: `log` matches `Album.LOG` but not `Album.backlog`. Before, `log` missed `.LOG` and matched `backlog`, for track-named files too.
- The Import Extra Files help text says so, and its examples name `lrc`, `cue` and `log`.

Unpackerr's `split_flac` turns a single-file FLAC or APE image with a cue sheet into track files beside the image; this branch doesn't change how Lidarr treats the image itself.

Tests, over the real services and test database: release files imported once into the album folder under their names (a UTF-16 cue sheet byte for byte, `.LOG` for `log`), with the track's lyrics still linked to the track; a track that didn't import keeps its lyrics out; a replacing import recycles the previous release's files and one that doesn't keeps them; nothing written over an existing file; extension matching by case and after a dot; the migration keeping rows and storing one without a track file; a release file moved with its album's tracks across disc folders, another album's left alone; recycling when a user or a scan removes the album's last track file, and not while one is left or for an import or a cleanup; housekeeping keeping an album's release file and dropping one whose album is gone. Each was broken on purpose and caught by a test.
