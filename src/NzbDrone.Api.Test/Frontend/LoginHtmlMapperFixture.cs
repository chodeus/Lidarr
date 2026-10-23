using System;
using FluentAssertions;
using Lidarr.Http.Frontend.Mappers;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Api.Test.Frontend
{
    [TestFixture]
    public class LoginHtmlMapperFixture
    {
        private Mock<IConfigFileProvider> _config;

        [SetUp]
        public void Setup()
        {
            _config = new Mock<IConfigFileProvider>();
            _config.SetupGet(c => c.AuthenticationMethod).Returns(AuthenticationType.Oidc);
            _config.SetupGet(c => c.UrlBase).Returns(string.Empty);
            _config.SetupGet(c => c.UiFolder).Returns("UI");
            _config.SetupGet(c => c.Theme).Returns("auto");
        }

        private string GetLoginHtml()
        {
            var diskProvider = new Mock<IDiskProvider>();
            diskProvider.Setup(d => d.ReadAllText(It.IsAny<string>())).Returns("<body class=\"auth-_AUTH_METHOD_\">");

            var mapper = new TestLoginHtmlMapper(Mock.Of<IAppFolderInfo>(a => a.StartUpFolder == "/app"),
                                                 diskProvider.Object,
                                                 new Lazy<ICacheBreakerProvider>(Mock.Of<ICacheBreakerProvider>),
                                                 _config.Object);

            return mapper.GetLoginHtml();
        }

        [Test]
        public void should_show_the_forms_login_when_oidc_is_not_configured()
        {
            GetLoginHtml().Should().Be("<body class=\"auth-forms oidc-misconfigured\">");
        }

        [Test]
        public void should_show_the_oidc_login_when_oidc_is_configured()
        {
            _config.SetupGet(c => c.OidcAuthority).Returns("https://auth.example.com/application/o/lidarr/");
            _config.SetupGet(c => c.OidcClientId).Returns("client-id");
            _config.SetupGet(c => c.OidcClientSecret).Returns("client-secret");
            _config.SetupGet(c => c.OidcUserIdentifier).Returns("user@example.com");
            _config.SetupGet(c => c.OidcScopes).Returns("openid profile email");

            GetLoginHtml().Should().Be("<body class=\"auth-oidc\">");
        }

        private class TestLoginHtmlMapper : LoginHtmlMapper
        {
            public TestLoginHtmlMapper(IAppFolderInfo appFolderInfo,
                                       IDiskProvider diskProvider,
                                       Lazy<ICacheBreakerProvider> cacheBreakProviderFactory,
                                       IConfigFileProvider configFileProvider)
                : base(appFolderInfo, diskProvider, cacheBreakProviderFactory, configFileProvider, LogManager.CreateNullLogger())
            {
            }

            public string GetLoginHtml() => GetHtmlText();
        }
    }
}
