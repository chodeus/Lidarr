# New: Match imported files tagged with an artist alias

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`. Reviewed in [#20](https://github.com/chodeus/Lidarr/pull/20).

Before this change, import identification scored the files' artist tag against the release artist's current MusicBrainz name only. Artist carries a large weight in the album distance, so files tagged with a former or alternate name took the full penalty. Live: Qobuz tags *Heard 'Em Say* and *Touch the Sky* as "Kanye West", MusicBrainz credits "Ye", and both failed to import at 23–25% distance against the 20% limit. Lidarr's own metadata already lists "Kanye West" among Ye's aliases, and "JAY Z" and "Jay-Z" among JAŸ-Z's.

- `Distance.AddStringToClosest` adds the penalty against the closest of several spellings. It shares the existing string penalty, which moves into `StringPenalty`, so `AddString` is unchanged. A separate name avoids an ambiguous call where existing callers pass `null`.
- `DistanceCalculator.AlbumReleaseDistance` passes the artist's name, with and without a leading "The" as artist lookups allow, and its aliases. Various Artists releases still skip the artist check, as before.
- An alias match counts a little worse than a name match (`Distance.AliasPenalty`, 0.05 of the artist weight), so the artist a name belongs to wins over another artist that has the name as an alias, such as a tribute act. An alias that cleans to nothing is ignored, so files without an artist tag keep the full penalty when the release artist has a name.

Tests: files tagged with an alias score only the alias penalty, and an artist whose tag matches no alias is still penalised. `ArtistAliasDistanceFixture` (re-audit, 2026-10) runs the real distance calculation on built releases: untagged files keep the full penalty when an alias cleans to nothing and the release artist has a name, a tribute act sharing the bare name as an alias doesn't beat the real artist, a tag without the leading "The" matches, case and accent variants match, and missing aliases are handled. Six mutations, all building, each fail a test.

## Summary by CodeRabbit

* **Bug Fixes**
  * Album release matching now checks artist aliases as well as the primary artist name, helping identify releases when a local track uses an alternate artist name.
  * Matching handles cases where artist names are missing, reducing incorrect distance scores in album comparisons.
