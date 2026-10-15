using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.AuthenticationTests
{
    [TestFixture]
    public class AuthenticationConfigurationExtensionsFixture : TestBase
    {
        private Mock<IConfigFileProvider> _config;

        [SetUp]
        public void Setup()
        {
            _config = new Mock<IConfigFileProvider>();
            _config.SetupGet(c => c.AuthenticationMethod).Returns(AuthenticationType.Oidc);
            _config.SetupGet(c => c.OidcAuthority).Returns("https://auth.example.com/application/o/lidarr/");
            _config.SetupGet(c => c.OidcClientId).Returns("client-id");
            _config.SetupGet(c => c.OidcClientSecret).Returns("client-secret");
            _config.SetupGet(c => c.OidcUserIdentifier).Returns("user@example.com");
            _config.SetupGet(c => c.OidcScopes).Returns("openid profile email");
        }

        [Test]
        public void should_be_configured_with_every_setting()
        {
            _config.Object.IsOidcConfigured().Should().BeTrue();
            _config.Object.EffectiveAuthenticationMethod().Should().Be(AuthenticationType.Oidc);
        }

        [TestCase("authority")]
        [TestCase("client id")]
        [TestCase("client secret")]
        [TestCase("user")]
        public void should_fall_back_to_forms_without_a_setting(string missing)
        {
            switch (missing)
            {
                case "authority":
                    _config.SetupGet(c => c.OidcAuthority).Returns(" ");
                    break;
                case "client id":
                    _config.SetupGet(c => c.OidcClientId).Returns(string.Empty);
                    break;
                case "client secret":
                    _config.SetupGet(c => c.OidcClientSecret).Returns((string)null);
                    break;
                default:
                    _config.SetupGet(c => c.OidcUserIdentifier).Returns(string.Empty);
                    break;
            }

            _config.Object.HasOidcSettings().Should().BeFalse();
            _config.Object.EffectiveAuthenticationMethod().Should().Be(AuthenticationType.Forms);
        }

        [TestCase("profile email")]
        [TestCase("openidprofile")]
        [TestCase("")]
        public void should_need_the_openid_scope(string scopes)
        {
            _config.SetupGet(c => c.OidcScopes).Returns(scopes);

            _config.Object.HasOidcSettings().Should().BeFalse();
        }

        [TestCase("http://auth.example.com/")]
        [TestCase("auth.example.com")]
        public void should_fall_back_to_forms_with_an_authority_that_is_not_https(string authority)
        {
            _config.SetupGet(c => c.OidcAuthority).Returns(authority);

            _config.Object.HasOidcSettings().Should().BeTrue();
            _config.Object.IsOidcAuthoritySecure().Should().BeFalse();
            _config.Object.EffectiveAuthenticationMethod().Should().Be(AuthenticationType.Forms);
        }

        [Test]
        public void should_accept_https_in_any_case()
        {
            _config.SetupGet(c => c.OidcAuthority).Returns("HTTPS://auth.example.com/");

            _config.Object.IsOidcAuthoritySecure().Should().BeTrue();
        }

        [TestCase(AuthenticationType.Forms)]
        [TestCase(AuthenticationType.External)]
        [TestCase(AuthenticationType.None)]
        public void should_leave_other_methods_alone(AuthenticationType method)
        {
            _config.SetupGet(c => c.AuthenticationMethod).Returns(method);
            _config.SetupGet(c => c.OidcClientId).Returns(string.Empty);

            _config.Object.EffectiveAuthenticationMethod().Should().Be(method);
        }

        [TestCase("openid profile email", new[] { "openid", "profile", "email" })]
        [TestCase("openid,profile , email", new[] { "openid", "profile", "email" })]
        [TestCase(null, new string[0])]
        public void should_split_scopes_on_spaces_and_commas(string scopes, string[] expected)
        {
            AuthenticationConfigurationExtensions.GetOidcScopes(scopes).Should().Equal(expected);
        }
    }
}
