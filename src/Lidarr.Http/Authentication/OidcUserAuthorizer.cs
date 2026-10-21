using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Json;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Core.Authentication;

namespace Lidarr.Http.Authentication
{
    // Only the configured user gets in: a setting with '@' matches an email the provider says is verified, any other the subject exactly
    public static class OidcUserAuthorizer
    {
        private static readonly Logger Logger = NzbDroneLogger.GetLogger(typeof(OidcUserAuthorizer));

        public static bool TryAuthorize(string configuredUser, ClaimsPrincipal principal, JsonElement? user)
        {
            if (configuredUser.IsNullOrWhiteSpace())
            {
                Logger.Error("No authorized users configured");

                return false;
            }

            if (!Matches(configuredUser, principal, user))
            {
                Logger.Error("User with subject '{0}' is not authorized to access this application", GetClaimValue(principal, user, "sub") ?? "unknown");

                return false;
            }

            var claims = new List<Claim>
            {
                new Claim("user", configuredUser),
                new Claim("AuthenticationType", nameof(AuthenticationType.Oidc)),
                new Claim("Provider", "OIDC")
            };

            principal?.AddIdentity(new ClaimsIdentity(claims));

            return true;
        }

        // Some providers let users choose their subject, so a subject must never pass for an email
        private static bool Matches(string configuredUser, ClaimsPrincipal principal, JsonElement? user)
        {
            if (configuredUser.Contains('@'))
            {
                return string.Equals(GetVerifiedEmail(principal, user), configuredUser, StringComparison.OrdinalIgnoreCase);
            }

            return string.Equals(GetClaimValue(principal, user, "sub"), configuredUser, StringComparison.Ordinal);
        }

        // The user info answer, when there is one, decides on its own whether the email counts
        private static string GetVerifiedEmail(ClaimsPrincipal principal, JsonElement? user)
        {
            if (user.HasValue && TryGetString(user.Value, "email", out var userInfoEmail))
            {
                return IsEmailVerified(user.Value) ? userInfoEmail : null;
            }

            var email = principal?.FindFirst("email")?.Value;

            if (email.IsNotNullOrWhiteSpace() &&
                bool.TryParse(principal?.FindFirst("email_verified")?.Value, out var verified) &&
                verified)
            {
                return email;
            }

            return null;
        }

        private static string GetClaimValue(ClaimsPrincipal principal, JsonElement? user, string claim)
        {
            if (user.HasValue && TryGetString(user.Value, claim, out var value))
            {
                return value;
            }

            return principal?.FindFirst(claim)?.Value;
        }

        private static bool TryGetString(JsonElement element, string property, out string value)
        {
            if (element.ValueKind == JsonValueKind.Object &&
                element.TryGetProperty(property, out var jsonValue) &&
                jsonValue.ValueKind == JsonValueKind.String)
            {
                value = jsonValue.GetString();

                return true;
            }

            value = null;

            return false;
        }

        private static bool IsEmailVerified(JsonElement user)
        {
            if (!user.TryGetProperty("email_verified", out var value))
            {
                return false;
            }

            return value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.String => bool.TryParse(value.GetString(), out var parsed) && parsed,
                _ => false
            };
        }
    }
}
