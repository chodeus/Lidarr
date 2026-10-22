# New: Match artists by exact alias before fuzzy matching

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`. Reviewed in [#18](https://github.com/chodeus/Lidarr/pull/18).

The parser resolves an artist name with an exact name match first, then `FindByNameInexact`. The inexact step already scores aliases, but only as its third fuzzy function. An earlier fuzzy match on another artist's name can win first, so an alias one letter off another artist's name goes to that artist.

This adds an exact alias check at the start of `FindByNameInexact`, before the fuzzy functions:
- It matches the cleaned title against each artist's cleaned aliases.
- An artist's own name always wins: if exactly one artist's cleaned name equals the title, with or without a leading "The", that artist is returned before any alias is checked (several such artists are left to the fuzzy matching). Cleaning keeps a leading "The", so without that a title "Zeta Orchestra" went to another artist's alias instead of "The Zeta Orchestra". Leaving the named artist to the fuzzy matching instead isn't enough: for a short name like "The AB" it can pick another artist.
- The cleaned aliases are worked out once per artist list, which `GetAllArtists` caches, rather than on every lookup; RSS sync looks up most release titles this way.
- An alias shared by more than one artist is ignored, and the fuzzy matching runs as before.

The three parser call sites already fall through to `FindByNameInexact`, so they don't change. Nothing outside the parser calls it.

Tests: an exact alias match (including case), name over alias, a shared alias, and alias over a fuzzy name match. `FindByAliasDbFixture` and `AliasPrecedenceFixture` (re-audit, 2026-10) run the real artist and parsing services over the test database: an artist named without or with its leading "The" beats another artist's alias, also for a release title, aliases match whatever their case, accents or ampersand, an alias that cleans to nothing never matches, repeated aliases still match, and an alias added after the artist list was read is found once the artist is updated. Seven mutations, all building, each fail a test.

## Summary by CodeRabbit

* **Bug Fixes**
  * Artist searches now recognize exact alias matches despite differences in capitalization, accents, or ampersand spelling.
  * When a title matches an artist’s name and another artist’s alias, the artist-name match takes precedence. An alias shared by several artists picks none of them.
  * Exact alias matches are preferred over fuzzy artist-name matches. Release titles that omit an artist’s leading “The” can still be matched to that artist.
