# New: Must Not Contain terms for metadata profiles

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`. Reviewed in [#3](https://github.com/chodeus/Lidarr/pull/3).

Adds Must Not Contain terms to metadata profiles (upstream request #4923):
- A refresh leaves out albums whose title contains a term as a whole word, case-insensitively. Albums with files, or added by hand, are kept.
- A term written as `/pattern/flags` is a regex anchored at both slashes. A plain term such as `Live // Unplugged` stays plain.
- Whole-word matching uses lookarounds, not `\b`, so terms that start or end with punctuation, like `(live)`, still match.
- The validator rejects invalid regexes and regexes that match an empty title.
- Migration 900 adds `MetadataProfiles.Ignored`. The 900+ range is reserved for build-only changes.

`SkyHookProxy` builds its own `TermMatcherService`, and `ITermMatcherService` is unchanged, because plugins construct `SkyHookProxy` with develop's constructor.

## Summary by CodeRabbit

* **New Features**
  * Metadata profiles now include a “Must Not Contain” list for excluding albums with specified title terms. Terms match as whole words, regardless of case, and regular-expression terms are supported.
  * Add terms using Tab or Enter. Matching terms are shown as labels in the profile.
* **Bug Fixes**
  * Invalid regular expressions and terms that match an empty title are rejected when saving a metadata profile.
