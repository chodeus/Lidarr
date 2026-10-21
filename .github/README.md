# Add OIDC sign in beside Forms

Part of the [custom Lidarr image](https://github.com/chodeus/Lidarr/tree/custom) (`ghcr.io/chodeus/lidarr:custom`), which merges this branch into `develop`. Reviewed in [#48](https://github.com/chodeus/Lidarr/pull/48).

- New authentication method, OIDC, beside Forms. Settings → General → Security takes the provider's Authority (must be `https://`), Client ID, Client Secret, the one user allowed in, and the scopes (default `openid profile email`; `openid` is required). They live in `config.xml` like the other auth settings, and environment options can set them.
- The login page shows a Login button that signs in with the provider (authorization code flow). Only the configured user gets in, matched by subject (`sub`) or by an email the provider says is verified; the user info answer decides when there is one. Sign out also signs out of the provider when it offers an end session endpoint, then shows a signed-out page.
- The provider's redirect URI is `<Lidarr URL>/signin-oidc`. Plain `http://` works. Behind a reverse proxy that serves https, put the proxy in Trusted Networks (loopback is trusted already), or the redirect URI comes out as `http://` and the provider rejects it.
- OIDC that isn't fully configured, or whose Authority isn't `https://`, falls back to Forms for the UI, so a bad config file can't leave the UI open. The login page then shows the Forms login with a note to check the OIDC settings. A health check reports missing settings, an insecure Authority, or a provider whose discovery document can't be read. Saving a changed Authority checks the discovery document too.
- The settings API never returns the client secret: it returns `********`, and saving that placeholder keeps the stored secret. After the Authority changes, the secret has to be entered again, so the stored one never goes to another provider.
- A settings save that leaves a setting out, such as one from a client built before the OIDC settings, keeps that setting's stored value, as the database settings already do. Before, it failed partway through the save.
- Changing the OIDC settings needs no restart. The Forms login now also reads the method on each request instead of once at startup.
- The API key and its query parameter are unchanged.
- `IConfigFileProvider` gains the OIDC settings with default bodies, and `HostConfigController` keeps develop's constructor. A rejected sign in logs the user's subject, not their email. Adds `Microsoft.AspNetCore.Authentication.OpenIdConnect` 8.0.31.

Tests: the configuration rules (every setting, `openid`, `https://`, the fallback to Forms, scope parsing), the discovery check, the health check, the allowed-user rules (subject, verified email from user info or the token, case), the UI policy's scheme, the login page in the fallback, the log line for a rejected user, the secret placeholder after an Authority change, the return URL kept inside the URL base, the container picking the controller constructor with the Authority check, the sign-in cookies over http and https, and a save that leaves the OIDC settings out. Each rule was broken on purpose and caught by a test. Not exercised against a live provider.

## Summary by CodeRabbit

* **New Features**
  * Added OpenID Connect (OIDC) as an authentication option, with settings for the provider, client credentials, user identifier, and scopes.
  * Added single sign-on login and a themed signed-out page, with support for returning to the requested page after login.
  * Logout is available for both form-based and OIDC authentication.
* **Bug Fixes**
  * Added configuration checks and health warnings for incomplete, insecure, or unreachable OIDC providers.
  * OIDC client secrets are masked in settings and preserved when left unchanged.
  * Added sign-in guidance when OIDC is misconfigured or a single sign-on attempt fails.
