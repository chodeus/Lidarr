using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Http.Authentication
{
    public class UiAuthorizationPolicyProvider : IAuthorizationPolicyProvider
    {
        private const string PolicyName = "UI";
        private readonly IConfigFileProvider _config;

        public DefaultAuthorizationPolicyProvider FallbackPolicyProvider { get; }

        public UiAuthorizationPolicyProvider(IOptions<AuthorizationOptions> options,
            IConfigFileProvider config)
        {
            FallbackPolicyProvider = new DefaultAuthorizationPolicyProvider(options);
            _config = config;
        }

        public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => FallbackPolicyProvider.GetDefaultPolicyAsync();

        public Task<AuthorizationPolicy> GetFallbackPolicyAsync() => FallbackPolicyProvider.GetFallbackPolicyAsync();

        public Task<AuthorizationPolicy> GetPolicyAsync(string policyName)
        {
            if (policyName.Equals(PolicyName, StringComparison.OrdinalIgnoreCase))
            {
                var authenticationMethod = _config.EffectiveAuthenticationMethod();

                // OIDC signs into the cookie scheme; using the OIDC scheme here would send every
                // unauthenticated request to the identity provider instead of the login page
                var scheme = authenticationMethod == AuthenticationType.Oidc
                    ? CookieAuthenticationDefaults.AuthenticationScheme
                    : authenticationMethod.ToString();

                var policy = new AuthorizationPolicyBuilder(scheme)
                    .AddRequirements(new BypassableDenyAnonymousAuthorizationRequirement());

                return Task.FromResult(policy.Build());
            }

            return FallbackPolicyProvider.GetPolicyAsync(policyName);
        }
    }
}
