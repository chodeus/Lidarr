using System.Security.Claims;
using System.Threading.Tasks;
using FluentAssertions;
using Lidarr.Http.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http.Dispatchers;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Configuration.Events;

namespace NzbDrone.Api.Test.Authentication
{
    [TestFixture]
    public class AuthenticationBuilderExtensionsFixture
    {
        [TestCase("http", false)]
        [TestCase("https", true)]
        public void should_mark_the_sign_in_cookies_secure_only_over_https(string scheme, bool secure)
        {
            var config = new Mock<IConfigFileProvider>();
            config.SetupGet(c => c.AuthenticationMethod).Returns(AuthenticationType.Oidc);
            config.SetupGet(c => c.OidcAuthority).Returns("https://auth.example.com/");
            config.SetupGet(c => c.OidcClientId).Returns("lidarr");
            config.SetupGet(c => c.OidcClientSecret).Returns("client-secret");
            config.SetupGet(c => c.OidcUserIdentifier).Returns("user@example.com");
            config.SetupGet(c => c.OidcScopes).Returns("openid email");
            config.SetupGet(c => c.InstanceName).Returns("Lidarr");
            config.SetupGet(c => c.UrlBase).Returns(string.Empty);

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDataProtection();
            services.AddSingleton(config.Object);
            services.AddSingleton(Mock.Of<ICertificateValidationService>());
            services.AddAppAuthentication();

            var options = services.BuildServiceProvider()
                                  .GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
                                  .Get(nameof(AuthenticationType.Oidc));

            var context = new DefaultHttpContext();
            context.Request.Scheme = scheme;
            context.Request.Host = new HostString("lidarr.example.com:8686");

            options.CorrelationCookie.Build(context).Secure.Should().Be(secure);
            options.NonceCookie.Build(context).Secure.Should().Be(secure);
        }

        [Test]
        public void should_reload_the_oidc_options_when_the_provider_settings_are_saved()
        {
            var config = new Mock<IConfigFileProvider>();
            config.SetupGet(c => c.AuthenticationMethod).Returns(AuthenticationType.Forms);
            config.SetupGet(c => c.OidcScopes).Returns("openid");
            config.SetupGet(c => c.InstanceName).Returns("Lidarr");
            config.SetupGet(c => c.UrlBase).Returns(string.Empty);

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDataProtection();
            services.AddSingleton(config.Object);
            services.AddSingleton(Mock.Of<ICertificateValidationService>());
            services.AddSingleton<OidcOptionsChangeTokenSource>();
            services.AddSingleton<IOptionsChangeTokenSource<OpenIdConnectOptions>>(s => s.GetRequiredService<OidcOptionsChangeTokenSource>());
            services.AddAppAuthentication();

            var provider = services.BuildServiceProvider();
            var monitor = provider.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>();

            monitor.Get(nameof(AuthenticationType.Oidc)).Authority.Should().Be("https://localhost");

            config.SetupGet(c => c.AuthenticationMethod).Returns(AuthenticationType.Oidc);
            config.SetupGet(c => c.OidcAuthority).Returns("https://auth.example.com/");
            config.SetupGet(c => c.OidcClientId).Returns("lidarr");
            config.SetupGet(c => c.OidcClientSecret).Returns("client-secret");
            config.SetupGet(c => c.OidcUserIdentifier).Returns("user@example.com");
            provider.GetRequiredService<OidcOptionsChangeTokenSource>().Handle(new ConfigFileSavedEvent());

            monitor.Get(nameof(AuthenticationType.Oidc)).Authority.Should().Be("https://auth.example.com/");
        }

        [TestCase("subject-1", "subject-1", true)]
        [TestCase("Subject-1", "subject-1", false)]
        [TestCase("user@example.com", "USER@example.com", true)]
        public async Task should_keep_a_session_only_for_the_configured_user(string configuredUser, string sessionUser, bool kept)
        {
            var config = new Mock<IConfigFileProvider>();
            config.SetupGet(c => c.AuthenticationMethod).Returns(AuthenticationType.Oidc);
            config.SetupGet(c => c.OidcAuthority).Returns("https://auth.example.com/");
            config.SetupGet(c => c.OidcClientId).Returns("lidarr");
            config.SetupGet(c => c.OidcClientSecret).Returns("client-secret");
            config.SetupGet(c => c.OidcUserIdentifier).Returns(configuredUser);
            config.SetupGet(c => c.OidcScopes).Returns("openid email");
            config.SetupGet(c => c.InstanceName).Returns("Lidarr");
            config.SetupGet(c => c.UrlBase).Returns(string.Empty);

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDataProtection();
            services.AddSingleton(config.Object);
            services.AddSingleton(Mock.Of<ICertificateValidationService>());
            services.AddAppAuthentication();

            var provider = services.BuildServiceProvider();
            var options = provider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("user", sessionUser) }, "Cookies"));
            var context = new CookieValidatePrincipalContext(
                new DefaultHttpContext { RequestServices = provider },
                new AuthenticationScheme(CookieAuthenticationDefaults.AuthenticationScheme, null, typeof(CookieAuthenticationHandler)),
                options,
                new AuthenticationTicket(principal, CookieAuthenticationDefaults.AuthenticationScheme));

            await options.Events.ValidatePrincipal(context);

            (context.Principal != null).Should().Be(kept);
        }
    }
}
