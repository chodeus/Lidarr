using System;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Authentication
{
    public static class AuthenticationConfigurationExtensions
    {
        public static bool IsOidcConfigured(this IConfigFileProvider configFileProvider)
        {
            return configFileProvider.HasOidcSettings() && configFileProvider.IsOidcAuthoritySecure();
        }

        public static bool HasOidcSettings(this IConfigFileProvider configFileProvider)
        {
            return configFileProvider.OidcAuthority.IsNotNullOrWhiteSpace() &&
                   configFileProvider.OidcClientId.IsNotNullOrWhiteSpace() &&
                   configFileProvider.OidcClientSecret.IsNotNullOrWhiteSpace() &&
                   configFileProvider.OidcUserIdentifier.IsNotNullOrWhiteSpace() &&
                   Array.IndexOf(GetOidcScopes(configFileProvider.OidcScopes), "openid") >= 0;
        }

        public static bool IsOidcAuthoritySecure(this IConfigFileProvider configFileProvider)
        {
            return configFileProvider.OidcAuthority.IsNotNullOrWhiteSpace() &&
                   configFileProvider.OidcAuthority.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        }

        public static string[] GetOidcScopes(string scopes)
        {
            return scopes?.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? Array.Empty<string>();
        }

        // OIDC that isn't fully configured falls back to Forms, so a bad config file can't lock the UI open
        public static AuthenticationType EffectiveAuthenticationMethod(this IConfigFileProvider configFileProvider)
        {
            var authenticationMethod = configFileProvider.AuthenticationMethod;

            return authenticationMethod == AuthenticationType.Oidc && !configFileProvider.IsOidcConfigured()
                ? AuthenticationType.Forms
                : authenticationMethod;
        }
    }
}
