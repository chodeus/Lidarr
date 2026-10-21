using FluentAssertions;
using Lidarr.Http.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http.Dispatchers;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;

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
    }
}
