using Moq;
using NUnit.Framework;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.HealthCheck.Checks;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.HealthCheck.Checks
{
    [TestFixture]
    public class OidcConfigurationCheckFixture : CoreTest<OidcConfigurationCheck>
    {
        private const string Authority = "https://auth.example.com/";

        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<ILocalizationService>()
                  .Setup(s => s.GetLocalizedString(It.IsAny<string>()))
                  .Returns("Some Warning Message");

            Mocker.GetMock<IConfigFileProvider>().SetupGet(c => c.AuthenticationMethod).Returns(AuthenticationType.Oidc);
            Mocker.GetMock<IConfigFileProvider>().SetupGet(c => c.OidcAuthority).Returns(Authority);
            Mocker.GetMock<IConfigFileProvider>().SetupGet(c => c.OidcClientId).Returns("client-id");
            Mocker.GetMock<IConfigFileProvider>().SetupGet(c => c.OidcClientSecret).Returns("client-secret");
            Mocker.GetMock<IConfigFileProvider>().SetupGet(c => c.OidcUserIdentifier).Returns("user@example.com");
            Mocker.GetMock<IConfigFileProvider>().SetupGet(c => c.OidcScopes).Returns("openid");

            Mocker.GetMock<IOidcDiscoveryService>().Setup(s => s.IsDiscoverable(Authority)).Returns(true);
        }

        [Test]
        public void should_be_ok_when_oidc_is_not_used()
        {
            Mocker.GetMock<IConfigFileProvider>().SetupGet(c => c.AuthenticationMethod).Returns(AuthenticationType.Forms);
            Mocker.GetMock<IConfigFileProvider>().SetupGet(c => c.OidcClientId).Returns(string.Empty);

            Subject.Check().ShouldBeOk();

            Mocker.GetMock<IOidcDiscoveryService>().Verify(s => s.IsDiscoverable(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_be_ok_with_a_reachable_provider()
        {
            Subject.Check().ShouldBeOk();
        }

        [Test]
        public void should_be_an_error_with_a_missing_setting()
        {
            Mocker.GetMock<IConfigFileProvider>().SetupGet(c => c.OidcClientSecret).Returns(string.Empty);

            Subject.Check().ShouldBeError(wikiFragment: "#oidc-not-configured");
        }

        [Test]
        public void should_be_an_error_with_an_authority_that_is_not_https()
        {
            Mocker.GetMock<IConfigFileProvider>().SetupGet(c => c.OidcAuthority).Returns("http://auth.example.com/");

            Subject.Check().ShouldBeError(wikiFragment: "#oidc-authority-not-secure");
        }

        [Test]
        public void should_be_an_error_when_the_provider_cannot_be_discovered()
        {
            Mocker.GetMock<IOidcDiscoveryService>().Setup(s => s.IsDiscoverable(Authority)).Returns(false);
            Mocker.GetMock<ILocalizationService>()
                  .Setup(s => s.GetLocalizedString(It.IsAny<string>(), It.IsAny<System.Collections.Generic.Dictionary<string, object>>()))
                  .Returns("Some Warning Message");

            Subject.Check().ShouldBeError(wikiFragment: "#oidc-configuration-unavailable");
        }
    }
}
