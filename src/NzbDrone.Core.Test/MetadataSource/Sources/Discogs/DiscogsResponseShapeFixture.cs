using System;
using System.Linq;
using System.Net;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Http;
using NzbDrone.Core.MetadataSource.Sources;
using NzbDrone.Core.MetadataSource.Sources.Discogs;
using NzbDrone.Core.Profiles.Metadata;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource.Sources.Discogs
{
    // The real DiscogsApi and DiscogsMetadataSource; only HTTP is mocked
    [TestFixture]
    public class DiscogsResponseShapeFixture : CoreTest<DiscogsMetadataSource>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<ICacheManager>(Mocker.Resolve<CacheManager>());
            Mocker.SetConstant<IDiscogsApi>(Mocker.Resolve<DiscogsApi>());
            Subject.Definition = new MetadataSourceDefinition { Settings = new DiscogsMetadataSourceSettings { Token = "placeholder-token", PressingsPerAlbum = 1 } };

            Mocker.GetMock<IMetadataProfileService>().Setup(s => s.Exists(1)).Returns(true);
            Mocker.GetMock<IMetadataProfileService>().Setup(s => s.Get(1)).Returns(MetadataProfileAlbumFilterFixture.AlbumsOnlyProfile());
        }

        private void Given<T>(Func<HttpRequest, string> json)
            where T : new()
        {
            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Get<T>(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(r => new HttpResponse<T>(new HttpResponse(r, new HttpHeader(), json(r), HttpStatusCode.OK)));
        }

        private static string Page(string pagination, params string[] entries)
        {
            var releases = string.Join(",", entries.Select(e => e.Split(':')).Select(p => "{\"id\":" + p[1] + ",\"type\":\"release\",\"role\":\"" + p[0] + "\",\"title\":\"R" + p[1] + "\",\"format\":\"CD, Album\"}"));
            return "{" + (pagination == null ? string.Empty : "\"pagination\":{\"pages\":" + pagination + "},") + "\"releases\":[" + releases + "]}";
        }

        private static bool OnPage(HttpRequest r, int page) => r.Url.Query.Contains("page=" + page + "&");

        [TestCase("")]
        [TestCase("null")]
        public void a_200_with_an_empty_body_is_not_a_not_found_for_an_album(string body)
        {
            Given<DiscogsMasterResource>(r => body);

            Assert.Throws<MetadataSourceUnavailableException>(() => Subject.GetAlbumInfo("discogs:master:10"));
        }

        [TestCase("")]
        [TestCase("null")]
        public void a_200_with_an_empty_body_is_not_a_not_found_for_an_artist(string body)
        {
            Given<DiscogsArtistResource>(r => body);

            Assert.Throws<MetadataSourceUnavailableException>(() => Subject.GetArtistInfo("discogs:artist:3", 1));
        }

        [Test]
        public void a_bootleg_before_another_role_on_page_one_is_not_dropped()
        {
            Given<DiscogsArtistReleasesPageResource>(r => OnPage(r, 1) ? Page("2", "Main:1", "UnofficialRelease:5", "Appearance:2") : Page("2", "Appearance:3", "UnofficialRelease:6"));

            Mocker.Resolve<DiscogsApi>().GetArtistBootlegs(3, null).Select(e => e.Id).Should().Equal(5, 6);
        }

        [TestCase("c1", 2)]
        [TestCase("d", 2)]
        public void lower_case_vinyl_sides_pair_into_discs(string position, int medium)
        {
            DiscogsMapper.MediumNumber(position).Should().Be(medium);
        }

        [Test]
        public void index_tracks_read_from_json_through_the_real_client()
        {
            Given<DiscogsMasterResource>(r => "{\"id\":10,\"title\":\"M\",\"main_release\":100,\"artists\":[{\"id\":3,\"name\":\"Placeholder Artist (2)\"}]}");
            Given<DiscogsReleaseResource>(r => "{\"id\":100,\"title\":\"M\",\"released\":\"1990-00-00\",\"artists\":[{\"id\":3,\"name\":\"Placeholder Artist (2)\"}],\"formats\":[{\"name\":\"CD\",\"descriptions\":[\"Album\"]}]," +
                                              "\"tracklist\":[{\"type_\":\"heading\",\"title\":\"Part\"},{\"type_\":\"index\",\"title\":\"Work\",\"sub_tracks\":[{\"type_\":\"track\",\"position\":\"1\",\"title\":\"I\",\"duration\":\"1:02:03\"},{\"type_\":\"track\",\"position\":\"2\",\"title\":\"II\"}]}]}");

            var (artistId, album, artists) = Subject.GetAlbumInfo("discogs:master:10");

            artistId.Should().Be("discogs:artist:3");
            artists.Single().Name.Should().Be("Placeholder Artist");
            var release = album.AlbumReleases.Value.Single();
            release.Monitored.Should().BeTrue();
            release.ReleaseDate.Should().Be(new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            release.Tracks.Value.Select(t => t.Title).Should().Equal("Work: I", "Work: II");
            release.Tracks.Value.Select(t => t.ForeignTrackId).Should().Equal("discogs:release:100:1", "discogs:release:100:2");
            release.Tracks.Value.First().Duration.Should().Be(3723000);
        }
    }
}
