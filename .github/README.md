# Add OIDC sign in beside Forms

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`. Reviewed in [#48](https://github.com/chodeus/Lidarr/pull/48) and [#64](https://github.com/chodeus/Lidarr/pull/64).

- New authentication method, OIDC, beside Forms. Settings → General → Security takes the provider's Authority (must be `https://`), Client ID, Client Secret, the one user allowed in, and the scopes (default `openid profile email`; `openid` is required). They live in `config.xml` like the other auth settings, and environment options can set them.
- The login page shows a Login button that signs in with the provider (authorization code flow). Only the configured user gets in. A setting containing `@` is matched only to an email the provider says is verified, ignoring case; any other is matched only to the subject (`sub`), exactly, so an account whose chosen subject looks like the email can't pass for it. A signed-in session is checked against the setting with the same rule, so changing the setting, even only its case for a subject, ends sessions for anyone else. The user info answer decides when there is one. Sign out also signs out of the provider when it offers an end session endpoint, then shows a signed-out page.
- The provider's redirect URI is `<Lidarr URL>/signin-oidc`, and its post-logout redirect URI is `<Lidarr URL>/signout-callback-oidc`; a provider that checks post-logout URIs rejects sign out without it. Plain `http://` works. Behind a reverse proxy that serves https, put the proxy in Trusted Networks (loopback is trusted already), or the redirect URI comes out as `http://` and the provider rejects it.
- OIDC that isn't fully configured, or whose Authority isn't `https://`, falls back to Forms for the UI, so a bad config file can't leave the UI open. The login page then shows the Forms login with a note to check the OIDC settings. A health check reports missing settings, an insecure Authority, or a provider whose discovery document can't be read. Saving a changed `https://` Authority checks the discovery document too; one that's rejected is never fetched.
- The settings API never returns the client secret: it returns `********`, and saving that placeholder keeps the stored secret. After the Authority changes, the secret has to be entered again, so the stored one never goes to another provider.
- A settings save that leaves a setting out, such as one from a client built before the OIDC settings, keeps that setting's stored value, as the database settings already do. Before, it failed partway through the save.
- Changing the OIDC settings needs no restart. The Forms login now also reads the method on each request instead of once at startup.
- The API key and its query parameter are unchanged.
- `IConfigFileProvider` gains the OIDC settings with default bodies, and `HostConfigController` keeps develop's constructor. The OIDC health check reasons are numbered from 1000, so develop's reasons keep their values. A rejected sign in logs the user's subject, not their email. Adds `Microsoft.AspNetCore.Authentication.OpenIdConnect` 8.0.31.

- The security settings section (`SecuritySettings`) and `FormInputButton` are TypeScript now, as in Radarr. `FormInputGroup` stays JavaScript, with `readOnly` and `onFocus` added to its prop types since it already passes them through.

Tests: the configuration rules (every setting, `openid`, `https://`, the fallback to Forms, scope parsing), the discovery check, the health check, the allowed-user rules (subject matched exactly, verified email from user info or the token ignoring case, a subject shaped like the configured email), the UI policy's scheme, the login page in the fallback, the log line for a rejected user, the secret placeholder after an Authority change, the return URL kept inside the URL base, the container picking the controller constructor with the Authority check, the sign-in cookies over http and https, a save that leaves the OIDC settings out, a session ended when the configured user changes, the settings reloading without a restart, no discovery fetch for a rejected Authority, and develop's health check reasons keeping their values. Each rule was broken on purpose and caught by a test. Not exercised against a live provider.

## Summary by CodeRabbit

* **New Features**
  * Added OpenID Connect (OIDC) sign-in with configurable provider details and support for returning to the requested page after login.
  * Added OIDC settings for provider authority, client ID and secret, user identifier, and scopes. Saved secrets remain masked.
  * Added a themed signed-out page and OIDC sign-out support.
  * Added login messages for OIDC setup issues and identity-provider sign-in failures.
* **Bug Fixes**
  * Incomplete, insecure or unreachable OIDC configurations are reported in health checks, and sign-in falls back to forms when OIDC is not fully configured.
  * OIDC access is limited to the configured user, with matching rules for subject identifiers and verified email addresses.
* **Documentation**
  * Added guidance for configuring OIDC sign-in, login and logout behavior, and compatibility notes.
