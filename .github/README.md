# Feature: Let an install use its own AcoustID key

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`. Reviewed in [#77](https://github.com/chodeus/Lidarr/pull/77). Stacked on `fix/acoustid-same-song`, which edits the same request line.

Every Lidarr install sends the same AcoustID application key, and AcoustID refuses it in runs (HTTP 429). A refused lookup leaves the files with no fingerprint results, so identification and any fingerprint check go on without them.

- Set `Lidarr__App__AcoustIdApiKey` in the container, or `AcoustIdApiKey` in config.xml, to an application key registered at acoustid.org; the environment wins, and a blank variable falls through to config.xml
- Unset keeps Lidarr's own key; if the setting can't be read, the lookup still runs on it and logs a warning
- `FingerprintingService` takes the setting through a second constructor, which the container picks; the three-argument one stays for plugins
- The new interface member has a default body, so develop's surface is unchanged

Summary by CodeRabbit:

- You can now provide a custom AcoustID API key in the app configuration. Nonblank keys are used for fingerprint lookups, with surrounding whitespace ignored.
- If no usable key is configured, the app continues to use its built-in key. If the configuration cannot be read, it logs a warning and falls back to the built-in key.

Tests: `ConfigFileProviderTest` reads the key from the environment before config.xml, lets a blank variable fall through and trims a set one. `FingerprintingServiceFixture` sends the install's key, sends Lidarr's own key byte for byte when none is set, and falls back to it when the setting throws. `ContainerFixture` resolves the service from the real container and checks it got the config file. Each of these mutations fails a test: ignoring the key, not trimming it, counting whitespace as a key, reading config.xml first, a blank variable hiding config.xml, an unreadable setting aborting the lookup, and a container that can't see the second constructor.
