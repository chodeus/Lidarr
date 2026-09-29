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

        private const string ReleaseGroupId = "3f8a1d2e-0000-4000-8000-000000000001";

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
        public void should_treat_a_release_group_without_a_credit_as_no_credit()
        {
            MusicBrainzArtistCreditProxy.Parse(@"{""id"":""x""}").Should().BeEmpty();
        }

        [Test]
        public void should_read_missing_fields_as_empty()
        {
            var names = MusicBrainzArtistCreditProxy.Parse(@"{""artist-credit"":[{""name"":null}]}");

            names.Should().ContainSingle();
            names[0].Name.Should().BeEmpty();
            names[0].ForeignArtistId.Should().BeEmpty();
            names[0].JoinPhrase.Should().BeEmpty();
        }

        [Test]
        public void should_not_ask_musicbrainz_about_an_id_it_did_not_issue()
        {
            Subject.GetCredit("deezer:12345").Should().BeEmpty();

            Mocker.GetMock<IHttpClient>().Verify(c => c.Get(It.IsAny<HttpRequest>()), Times.Never());
        }

        [Test]
        public void should_not_follow_redirects()
        {
            GivenResponse(HttpStatusCode.OK, Json);

            Subject.GetCredit(ReleaseGroupId);

            Mocker.GetMock<IHttpClient>().Verify(c => c.Get(It.Is<HttpRequest>(r => !r.AllowAutoRedirect && r.Url.FullUri.Contains(ReleaseGroupId))), Times.Once());
        }

        [Test]
        public void should_return_the_credit()
        {
            GivenResponse(HttpStatusCode.OK, Json);

            Subject.GetCredit(ReleaseGroupId).Should().HaveCount(2);
        }

        [Test]
        public void should_return_an_empty_credit_for_an_unknown_release_group()
        {
            GivenResponse(HttpStatusCode.NotFound);

            Subject.GetCredit(ReleaseGroupId).Should().BeEmpty();
        }

        [Test]
        public void should_return_null_when_musicbrainz_is_unavailable()
        {
            GivenResponse(HttpStatusCode.ServiceUnavailable);

            Subject.GetCredit(ReleaseGroupId).Should().BeNull();
        }

        [Test]
        public void should_return_null_when_the_request_fails()
        {
            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.Get(It.IsAny<HttpRequest>()))
                  .Throws(new WebException("timeout"));

            Subject.GetCredit(ReleaseGroupId).Should().BeNull();
        }
    }
}
