using System.Net;
using System.Text;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.AuthenticationTests
{
    [TestFixture]
    public class OidcDiscoveryServiceFixture : CoreTest<OidcDiscoveryService>
    {
        private const string Authority = "https://auth.example.com/application/o/lidarr/";

        private void GivenResponse(HttpStatusCode status, string json)
        {
            Mocker.GetMock<IHttpClient>()
                  .Setup(s => s.Get<OidcDiscoveryDocument>(It.IsAny<HttpRequest>()))
                  .Returns<HttpRequest>(r => new HttpResponse<OidcDiscoveryDocument>(new HttpResponse(r, new HttpHeader(), Encoding.UTF8.GetBytes(json), status)));
        }

        [Test]
        public void should_find_a_provider_with_both_endpoints()
        {
            GivenResponse(HttpStatusCode.OK, "{\"authorization_endpoint\":\"https://auth.example.com/authorize\",\"token_endpoint\":\"https://auth.example.com/token\"}");

            Subject.IsDiscoverable(Authority).Should().BeTrue();

            Mocker.GetMock<IHttpClient>()
                  .Verify(s => s.Get<OidcDiscoveryDocument>(It.Is<HttpRequest>(r => r.Url.FullUri == Authority + ".well-known/openid-configuration")), Times.Once());
        }

        [TestCase("{\"authorization_endpoint\":\"https://auth.example.com/authorize\"}")]
        [TestCase("{\"token_endpoint\":\"https://auth.example.com/token\"}")]
        [TestCase("null")]
        public void should_refuse_a_document_without_both_endpoints(string json)
        {
            GivenResponse(HttpStatusCode.OK, json);

            Subject.IsDiscoverable(Authority).Should().BeFalse();
        }

        [Test]
        public void should_refuse_an_http_error()
        {
            GivenResponse(HttpStatusCode.NotFound, "{\"authorization_endpoint\":\"https://auth.example.com/authorize\",\"token_endpoint\":\"https://auth.example.com/token\"}");

            Subject.IsDiscoverable(Authority).Should().BeFalse();
        }

        [Test]
        public void should_refuse_a_provider_that_cannot_be_reached()
        {
            Mocker.GetMock<IHttpClient>()
                  .Setup(s => s.Get<OidcDiscoveryDocument>(It.IsAny<HttpRequest>()))
                  .Throws(new WebException("Name or service not known"));

            Subject.IsDiscoverable(Authority).Should().BeFalse();
        }

        [TestCase(null)]
        [TestCase(" ")]
        public void should_not_ask_without_an_authority(string authority)
        {
            Subject.IsDiscoverable(authority).Should().BeFalse();

            Mocker.GetMock<IHttpClient>()
                  .Verify(s => s.Get<OidcDiscoveryDocument>(It.IsAny<HttpRequest>()), Times.Never());
        }
    }
}
