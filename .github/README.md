# New: Must Not Contain terms for metadata profiles

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`. Builds on [`feat/metadata-supplementary-sources`](https://github.com/chodeus/Lidarr/tree/feat/metadata-supplementary-sources), the top of the metadata sources stack. Reviewed in [#3](https://github.com/chodeus/Lidarr/pull/3) and [#69](https://github.com/chodeus/Lidarr/pull/69).

Adds Must Not Contain terms to metadata profiles (upstream request #4923):
- A refresh leaves out albums whose title contains a term as a whole word, case-insensitively. Albums with files, or added by hand, are kept.
- The terms apply to every metadata source's albums (Deezer, Discogs and any other), checked in the metadata source aggregator after the sources answer, and again after the supplementary sources add theirs.
- A term written as `/pattern/flags` is a regex anchored at both slashes, matched exactly as written (add `i` to ignore case; word boundaries are up to the pattern). A plain term such as `Live // Unplugged` stays plain. A regex that takes over 100 ms on a title counts as no match, with a warning, so the album is kept.
- Whole-word matching uses lookarounds, not `\b`, and only on a side where the term starts or ends with a word character (a letter, digit or underscore), so terms that start or end with punctuation, like `(live)`, still match, also right after a letter (`Album(Live)`). Titles and plain terms are compared in composed Unicode, so `Café` matches a title sent as `Cafe` plus a combining accent.
- Albums are filtered before the supplementary sources add theirs and again after, so a left-out edition can't make a supplement skip the album it is an edition of.
- The validator rejects invalid regexes and regexes that match an empty title.
- Migration 900 adds `MetadataProfiles.Ignored`. The 900+ range is reserved for build-only changes.
- Also merges [`fix/keep-unlisted-metadata`](https://github.com/chodeus/Lidarr/tree/fix/keep-unlisted-metadata), which keeps an album the artist's list leaves out unless the source moved it or the metadata profile excludes it. That check now uses the shared album filter, so a Must Not Contain term removes such an album too.

`SkyHookProxy` is unchanged apart from using the shared album filter, so plugins still construct it with develop's constructor. The aggregator builds its own `TermMatcherService`, since the whole-word match isn't on `ITermMatcherService`.

## Summary by CodeRabbit

* **New Features**
  * Metadata profiles now include a “Must Not Contain” list for excluding albums with specified title terms. Terms match as whole words, regardless of case, and regular-expression terms are supported.
  * Add terms using Tab or Enter. Matching terms are shown as labels in the profile.
* **Bug Fixes**
  * Invalid regular expressions and terms that match an empty title are rejected when saving a metadata profile.
