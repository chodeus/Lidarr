# New: Match imported files tagged with an artist alias

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`. Reviewed in [#20](https://github.com/chodeus/Lidarr/pull/20).

Import identification scores the files' artist tag against the release artist's current MusicBrainz name only. Artist carries the heaviest weight in the album distance, so files tagged with a former or alternate name take the full penalty. Live: Qobuz tags *Heard 'Em Say* and *Touch the Sky* as "Kanye West", MusicBrainz credits "Ye", and both failed to import at 23–25% distance against the 20% limit. Lidarr's own metadata already lists "Kanye West" among Ye's aliases, and "JAY Z" and "Jay-Z" among JAŸ-Z's.

- `Distance.AddStringToClosest` adds the penalty against the closest of several spellings. It shares the existing string penalty, which moves into `StringPenalty`, so `AddString` is unchanged. A separate name avoids an ambiguous call where existing callers pass `null`.
- `DistanceCalculator.AlbumReleaseDistance` passes the artist's name plus its aliases. Various Artists releases still skip the artist check, as before.

Tests: files tagged with an alias score no artist penalty, and an artist whose tag matches no alias is still penalised. Two mutations (ignore the aliases, take the farthest spelling) each fail a test.

## Summary by CodeRabbit

* **Bug Fixes**
  * Album release matching now checks artist aliases as well as the primary artist name, helping identify releases when a local track uses an alternate artist name.
  * Matching handles cases where artist names are missing, reducing incorrect distance scores in album comparisons.
