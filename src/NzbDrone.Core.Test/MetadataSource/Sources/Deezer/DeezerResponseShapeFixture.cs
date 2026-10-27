using System;
using System.Linq;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.MetadataSource.Sources;
using NzbDrone.Core.MetadataSource.Sources.Deezer;
using NzbDrone.Core.Profiles.Metadata;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource.Sources.Deezer
{
    // The real DeezerMetadataSource over the real DeezerApi, fed canned HTTP replies
    [TestFixture]
    public class DeezerResponseShapeFixture : CoreTest<DeezerMetadataSource>
    {
        private const string Artist10 = "{\"id\":10,\"name\":\"Placeholder Artist\"}";

        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<IDeezerApi>(Mocker.Resolve<DeezerApi>());
            Mocker.GetMock<IMetadataProfileService>().Setup(s => s.Exists(1)).Returns(true);
            Mocker.GetMock<IMetadataProfileService>().Setup(s => s.Get(1)).Returns(MetadataProfileAlbumFilterFixture.AlbumsOnlyProfile());
        }

        private void GivenResponse<T>(Func<HttpRequest, string> json)
            where T : new()
        {
            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Get<T>(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(r => new HttpResponse<T>(new HttpResponse(r, new HttpHeader(), json(r))));
        }

        private void GivenAlbumPages(Func<HttpRequest, string> json)
        {
            GivenResponse<DeezerArtistResource>(r => Artist10);
            GivenResponse<DeezerPageResource<DeezerAlbumResource>>(json);
        }

        [TestCase("")]
        [TestCase("null")]
        public void an_empty_artist_response_is_an_outage_not_a_not_found(string body)
        {
            GivenResponse<DeezerArtistResource>(r => body);

            Assert.Throws<MetadataSourceUnavailableException>(() => Subject.GetArtistInfo("deezer:10", 1));
        }

        [TestCase("")]
        [TestCase("null")]
        public void an_empty_album_response_is_an_outage_not_a_not_found(string body)
        {
            GivenResponse<DeezerAlbumResource>(r => body);

            Assert.Throws<MetadataSourceUnavailableException>(() => Subject.GetAlbumInfo("deezer:1"));
        }

        [Test]
        public void a_later_page_without_data_is_an_outage_not_a_short_album_list()
        {
            GivenAlbumPages(r => r.Url.Query.Contains("index=0")
                ? "{\"data\":[" + string.Join(",", Enumerable.Range(1, 100).Select(i => "{\"id\":" + i + ",\"record_type\":\"album\",\"title\":\"A\"}")) + "],\"total\":150,\"next\":\"https://api.deezer.com/artist/10/albums?index=100\"}"
                : "{}");

            Assert.Throws<MetadataSourceUnavailableException>(() => Subject.GetArtistInfo("deezer:10", 1));
        }

        [Test]
        public void a_short_last_page_without_its_total_is_an_outage()
        {
            GivenAlbumPages(r => r.Url.Query.Contains("index=0")
                ? "{\"data\":[" + string.Join(",", Enumerable.Range(1, 100).Select(i => "{\"id\":" + i + ",\"record_type\":\"album\",\"title\":\"A\"}")) + "],\"total\":150,\"next\":\"https://api.deezer.com/artist/10/albums?index=100\"}"
                : "{\"data\":[{\"id\":101,\"record_type\":\"album\",\"title\":\"A\"}]}");

            Assert.Throws<MetadataSourceUnavailableException>(() => Subject.GetArtistInfo("deezer:10", 1));
        }

        [Test]
        public void a_total_larger_than_the_items_returned_is_an_outage()
        {
            GivenAlbumPages(r => "{\"data\":[{\"id\":1,\"record_type\":\"album\",\"title\":\"A\"}],\"total\":3}");

            Assert.Throws<MetadataSourceUnavailableException>(() => Subject.GetArtistInfo("deezer:10", 1));
        }

        [Test]
        public void an_empty_page_that_points_to_a_next_one_is_an_outage()
        {
            GivenAlbumPages(r => r.Url.Query.Contains("index=0")
                ? "{\"data\":[],\"next\":\"https://api.deezer.com/artist/10/albums?index=100\"}"
                : "{\"data\":[{\"id\":1,\"record_type\":\"album\",\"title\":\"A\"}],\"total\":1}");

            Assert.Throws<MetadataSourceUnavailableException>(() => Subject.GetArtistInfo("deezer:10", 1));
        }

        [Test]
        public void a_null_data_page_is_reported_as_unavailable()
        {
            GivenAlbumPages(r => "{\"data\":null,\"total\":3}");

            Assert.Throws<MetadataSourceUnavailableException>(() => Subject.GetArtistInfo("deezer:10", 1));
        }

        [Test]
        public void an_album_whose_track_list_comes_back_empty_is_an_outage()
        {
            GivenResponse<DeezerAlbumResource>(r => "{\"id\":1,\"title\":\"A\",\"record_type\":\"album\",\"nb_tracks\":12,\"artist\":" + Artist10 + "}");
            GivenResponse<DeezerPageResource<DeezerTrackResource>>(r => "{\"data\":[],\"total\":12}");

            Assert.Throws<MetadataSourceUnavailableException>(() => Subject.GetAlbumInfo("deezer:1"));
        }
    }
}
