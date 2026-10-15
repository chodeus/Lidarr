# Treat a release date in year 1 as no date

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`. Reviewed in [#45](https://github.com/chodeus/Lidarr/pull/45).

- The metadata server sends `0001-01-01` as the release date of a release group with no date. The year matcher read that as year 1, so any release with a year in its title was past the hard-reject limit ("Release year 2019 does not match album year 1"). Year 1 now counts as no date: a match with no bonus or penalty, as for a missing date. This also covers the edition release dates the matcher checks and the year score used to rank releases.
- For an undated album, the matcher checks dated editions first. It uses the no-date match only if no edition is within a year. An undated compilation or live album gets no year penalty.
- An album search no longer sends year 1 as the album year. The Newznab text search for a self-titled album adds the year only when there is one; before, an undated album searched for `Artist Artist 1`, or `Artist Artist 0` with no date at all.
- The stored date stays `0001-01-01`: artist statistics count an album as available by `ReleaseDate <= now`, so a null would make these albums look unreleased.

Tests: the matcher with year 1, dated editions on an undated album, an undated compilation, the album search year for a dated and an undated album, and the self-titled text search with and without a year. Each rule was broken on purpose and caught by a test.

## Summary by CodeRabbit

* **Bug Fixes**
  * Albums with missing dates or placeholder year-one dates are treated as having no usable year. Search queries omit the year in these cases, while valid album years continue to be included. Album matching avoids penalizing year differences when no usable release year is available, and prefers editions with a valid date when choosing a match.
