using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;
using NzbDrone.Http.Authentication;
using NzbDrone.Test.Common;

namespace NzbDrone.Api.Test.Authentication
{
    [TestFixture]
    public class UiAuthorizationPolicyProviderFixture : TestBase<UiAuthorizationPolicyProvider>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<IOptions<AuthorizationOptions>>(Options.Create(new AuthorizationOptions()));

            Mocker.GetMock<IConfigFileProvider>().SetupGet(c => c.AuthenticationMethod).Returns(AuthenticationType.Oidc);
            Mocker.GetMock<IConfigFileProvider>().SetupGet(c => c.OidcAuthority).Returns("https://auth.example.com/");
            Mocker.GetMock<IConfigFileProvider>().SetupGet(c => c.OidcClientId).Returns("client-id");
            Mocker.GetMock<IConfigFileProvider>().SetupGet(c => c.OidcClientSecret).Returns("client-secret");
            Mocker.GetMock<IConfigFileProvider>().SetupGet(c => c.OidcUserIdentifier).Returns("user@example.com");
            Mocker.GetMock<IConfigFileProvider>().SetupGet(c => c.OidcScopes).Returns("openid");
        }

        private string UiScheme()
        {
            return Subject.GetPolicyAsync("UI").GetAwaiter().GetResult().AuthenticationSchemes.Should().ContainSingle().Subject;
        }

        [Test]
        public void should_check_the_cookie_for_oidc_instead_of_the_identity_provider()
        {
            UiScheme().Should().Be("Cookies");
        }

        [Test]
        public void should_check_forms_when_oidc_is_not_fully_configured()
        {
            Mocker.GetMock<IConfigFileProvider>().SetupGet(c => c.OidcClientSecret).Returns(string.Empty);

            UiScheme().Should().Be("Forms");
        }

        [TestCase(AuthenticationType.Forms, "Forms")]
        [TestCase(AuthenticationType.External, "External")]
        public void should_check_the_methods_own_scheme(AuthenticationType method, string scheme)
        {
            Mocker.GetMock<IConfigFileProvider>().SetupGet(c => c.AuthenticationMethod).Returns(method);

            UiScheme().Should().Be(scheme);
        }
    }
}
