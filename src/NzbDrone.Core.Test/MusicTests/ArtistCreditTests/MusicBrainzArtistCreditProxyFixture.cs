using System.Net;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Music.ArtistCredits;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MusicTests.ArtistCreditTests
{
    [TestFixture]
    public class MusicBrainzArtistCreditProxyFixture : CoreTest<MusicBrainzArtistCreditProxy>
    {
        private const string Json = @"{""artist-credit"":[
            {""name"":""Artist Name"",""joinphrase"":"" feat. "",""artist"":{""id"":""primary-id"",""name"":""Artist Name""}},
            {""name"":""Credited Guest"",""joinphrase"":"""",""artist"":{""id"":""guest-id"",""name"":""Canonical Guest""}}]}";

        private void GivenResponse(HttpStatusCode status, string content = "")
        {
            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.Get(It.IsAny<HttpRequest>()))
                  .Returns<HttpRequest>(r => new HttpResponse(r, new HttpHeader(), content, status));
        }

        [Test]
        public void should_parse_the_credited_names_and_join_phrases()
        {
            var names = MusicBrainzArtistCreditProxy.Parse(Json);

            names.Should().HaveCount(2);
            names[0].JoinPhrase.Should().Be(" feat. ");
            names[1].Name.Should().Be("Credited Guest");
            names[1].ForeignArtistId.Should().Be("guest-id");
        }

        [Test]
        public void should_return_the_credit()
        {
            GivenResponse(HttpStatusCode.OK, Json);

            Subject.GetCredit("release-group-id").Should().HaveCount(2);
        }

        [Test]
        public void should_return_an_empty_credit_for_an_unknown_release_group()
        {
            GivenResponse(HttpStatusCode.NotFound);

            Subject.GetCredit("release-group-id").Should().BeEmpty();
        }

        [Test]
        public void should_return_null_when_musicbrainz_is_unavailable()
        {
            GivenResponse(HttpStatusCode.ServiceUnavailable);

            Subject.GetCredit("release-group-id").Should().BeNull();
        }

        [Test]
        public void should_return_null_when_the_request_fails()
        {
            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.Get(It.IsAny<HttpRequest>()))
                  .Throws(new WebException("timeout"));

            Subject.GetCredit("release-group-id").Should().BeNull();
        }
    }
}
