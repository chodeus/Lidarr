using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.MetadataSource.Sources;
using NzbDrone.Core.MetadataSource.Sources.Deezer;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource.Sources.Deezer
{
    [TestFixture]
    public class DeezerApiFixture : CoreTest<DeezerApi>
    {
        private readonly List<HttpRequest> _requests = new();

        [SetUp]
        public void Setup()
        {
            _requests.Clear();
        }

        private void GivenResponse<T>(Func<HttpRequest, string> json)
            where T : new()
        {
            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Get<T>(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(r => _requests.Add(r))
                .Returns<HttpRequest>(r => new HttpResponse<T>(new HttpResponse(r, new HttpHeader(), json(r))));
        }

        [Test]
        public void a_deezer_not_found_is_null()
        {
            GivenResponse<DeezerArtistResource>(r => "{\"error\":{\"type\":\"DataException\",\"message\":\"no data\",\"code\":800}}");

            Subject.GetArtist(1).Should().BeNull();
        }

        [Test]
        public void any_other_deezer_error_means_deezer_is_unavailable()
        {
            GivenResponse<DeezerArtistResource>(r => "{\"error\":{\"type\":\"Exception\",\"message\":\"Quota limit exceeded\",\"code\":4}}");

            Assert.Throws<MetadataSourceUnavailableException>(() => Subject.GetArtist(1));
        }

        [Test]
        public void a_failed_request_means_deezer_is_unavailable()
        {
            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Get<DeezerArtistResource>(It.IsAny<HttpRequest>()))
                .Throws(new WebException("Connection refused"));

            Assert.Throws<MetadataSourceUnavailableException>(() => Subject.GetArtist(1));
        }

        [Test]
        public void artist_albums_are_read_page_by_page_within_the_rate_limit()
        {
            GivenResponse<DeezerPageResource<DeezerAlbumResource>>(r => r.Url.Query.Contains("index=0")
                ? "{\"data\":[{\"id\":1}],\"total\":2,\"next\":\"https://api.deezer.com/artist/1/albums?index=100\"}"
                : "{\"data\":[{\"id\":2}],\"total\":2}");

            Subject.GetArtistAlbums(1).Select(a => a.Id).Should().Equal(1, 2);

            _requests.Select(r => r.Url.Query).Should().Equal("index=0&limit=100", "index=100&limit=100");
            _requests.Should().OnlyContain(r => r.RateLimit > TimeSpan.Zero);
        }

        [TestCase("index=0")]
        [TestCase("index=100")]
        public void a_missing_page_means_deezer_is_unavailable(string missingPage)
        {
            GivenResponse<DeezerPageResource<DeezerTrackResource>>(r => r.Url.Query.Contains(missingPage)
                ? "{\"error\":{\"type\":\"DataException\",\"message\":\"no data\",\"code\":800}}"
                : "{\"data\":[{\"id\":1}],\"total\":200,\"next\":\"https://api.deezer.com/album/1/tracks?index=100\"}");

            Assert.Throws<MetadataSourceUnavailableException>(() => Subject.GetAlbumTracks(1));
        }

        [Test]
        public void a_list_past_the_page_limit_means_deezer_is_unavailable()
        {
            GivenResponse<DeezerPageResource<DeezerAlbumResource>>(r => "{\"data\":[{\"id\":1}],\"next\":\"https://api.deezer.com/artist/1/albums?index=1\"}");

            Assert.Throws<MetadataSourceUnavailableException>(() => Subject.GetArtistAlbums(1));
        }
    }
}
