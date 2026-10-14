using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MetadataSource.Sources;
using NzbDrone.Core.MetadataSource.Sources.Discogs;
using NzbDrone.Core.Music;
using NzbDrone.Core.Profiles.Metadata;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource.Sources.Discogs
{
    [TestFixture]
    public class DiscogsMetadataSourceFixture : CoreTest<DiscogsMetadataSource>
    {
        private static readonly DiscogsArtistCreditResource Credit = new() { Id = 3, Name = "Placeholder Artist" };

        private DiscogsMetadataSourceSettings _settings;

        [SetUp]
        public void Setup()
        {
            _settings = new DiscogsMetadataSourceSettings { Token = "placeholder-token" };
            Mocker.SetConstant<ICacheManager>(Mocker.Resolve<CacheManager>());
            Subject.Definition = new MetadataSourceDefinition { Settings = _settings };

            Mocker.GetMock<IMetadataProfileService>().Setup(s => s.Exists(1)).Returns(true);
            Mocker.GetMock<IMetadataProfileService>().Setup(s => s.Get(1)).Returns(MetadataProfileAlbumFilterFixture.AlbumsOnlyProfile());
            Mocker.GetMock<IDiscogsApi>().Setup(a => a.GetArtist(3, It.IsAny<string>())).Returns(new DiscogsArtistResource { Id = 3, Name = "Placeholder Artist", Uri = "https://www.discogs.com/artist/3" });
        }

        private static DiscogsReleaseResource Release(long id, string format, params string[] descriptions)
        {
            return new DiscogsReleaseResource
            {
                Id = id,
                Title = "Release " + id,
                Year = 2001,
                Artists = new List<DiscogsArtistCreditResource> { Credit },
                Formats = new List<DiscogsFormatResource> { new() { Name = format, Descriptions = descriptions.ToList() } },
                Tracklist = new List<DiscogsTrackResource> { new() { Position = "1", Type = "track", Title = "Track" } }
            };
        }

        private void GivenMaster(long id, long mainRelease, params (long Id, int InCollection)[] versions)
        {
            Mocker.GetMock<IDiscogsApi>().Setup(a => a.GetMaster(id, It.IsAny<string>()))
                .Returns(new DiscogsMasterResource { Id = id, Title = "Master " + id, Year = 2001, MainRelease = mainRelease, Artists = new List<DiscogsArtistCreditResource> { Credit } });
            Mocker.GetMock<IDiscogsApi>().Setup(a => a.GetMasterVersions(id, It.IsAny<string>()))
                .Returns(versions.Select(v => new DiscogsVersionResource { Id = v.Id, MajorFormats = new List<string> { "CD" }, Stats = new DiscogsVersionStatsResource { Community = new DiscogsCommunityResource { InCollection = v.InCollection } } }).ToList());

            foreach (var releaseId in versions.Select(v => v.Id).Append(mainRelease))
            {
                Mocker.GetMock<IDiscogsApi>().Setup(a => a.GetRelease(releaseId, It.IsAny<string>())).Returns(Release(releaseId, "CD", "Album"));
            }
        }

        [TestCase("discogs:artist:3", true)]
        [TestCase("discogs:master:3", true)]
        [TestCase("deezer:3", false)]
        public void handles_only_discogs_ids(string foreignId, bool expected)
        {
            Subject.CanHandleId(foreignId).Should().Be(expected);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void bootlegs_are_read_only_when_the_profile_allows_them(bool allowed)
        {
            var profile = MetadataProfileAlbumFilterFixture.AlbumsOnlyProfile();
            profile.ReleaseStatuses.Single(s => s.ReleaseStatus.Name == "Bootleg").Allowed = allowed;
            Mocker.GetMock<IMetadataProfileService>().Setup(s => s.Get(1)).Returns(profile);
            Mocker.GetMock<IDiscogsApi>().Setup(a => a.GetArtistOwnReleases(3, It.IsAny<string>())).Returns(new List<DiscogsArtistReleaseResource>
            {
                new() { Id = 15, Type = "release", Role = "Main", Title = "Standalone", Format = "CD, Album" }
            });
            Mocker.GetMock<IDiscogsApi>().Setup(a => a.GetArtistBootlegs(3, It.IsAny<string>())).Returns(new List<DiscogsArtistReleaseResource>
            {
                new() { Id = 16, Type = "release", Role = "UnofficialRelease", Title = "Bootleg", Format = "CD, Album, Unofficial" }
            });

            var artist = Subject.GetArtistInfo("discogs:artist:3", 1);

            artist.Albums.Value.Select(a => a.ForeignAlbumId).Should().Equal(allowed ? new[] { "discogs:release:15", "discogs:release:16" } : new[] { "discogs:release:15" });
            Mocker.GetMock<IDiscogsApi>().Verify(a => a.GetArtistBootlegs(3, It.IsAny<string>()), allowed ? Times.Once() : Times.Never());
        }

        [Test]
        public void a_bootleg_credit_counts_as_a_bootleg_whatever_its_format_says()
        {
            var profile = MetadataProfileAlbumFilterFixture.AlbumsOnlyProfile();
            profile.ReleaseStatuses.Single(s => s.ReleaseStatus.Name == "Official").Allowed = false;
            profile.ReleaseStatuses.Single(s => s.ReleaseStatus.Name == "Bootleg").Allowed = true;
            Mocker.GetMock<IMetadataProfileService>().Setup(s => s.Get(1)).Returns(profile);
            Mocker.GetMock<IDiscogsApi>().Setup(a => a.GetArtistOwnReleases(3, It.IsAny<string>())).Returns(new List<DiscogsArtistReleaseResource>());
            Mocker.GetMock<IDiscogsApi>().Setup(a => a.GetArtistBootlegs(3, It.IsAny<string>())).Returns(new List<DiscogsArtistReleaseResource>
            {
                new() { Id = 17, Type = "release", Role = "UnofficialRelease", Title = "Bootleg", Format = "CD + DVD-V + Unofficial" }
            });

            Subject.GetArtistInfo("discogs:artist:3", 1).Albums.Value.Select(a => a.ForeignAlbumId).Should().Equal("discogs:release:17");
        }

        [Test]
        public void artist_info_keeps_the_artists_own_audio_releases_the_profile_allows()
        {
            Mocker.GetMock<IDiscogsApi>().Setup(a => a.GetArtistOwnReleases(3, It.IsAny<string>())).Returns(new List<DiscogsArtistReleaseResource>
            {
                new() { Id = 10, Type = "master", Role = "Main", Title = "Album", MainRelease = 100 },
                new() { Id = 11, Type = "master", Role = "Main", Title = "Single", MainRelease = 110 },
                new() { Id = 12, Type = "release", Role = "Main", Title = "Promo", Format = "CD, Album, Promo" },
                new() { Id = 13, Type = "release", Role = "Main", Title = "Video", Format = "VHS, Album" },
                new() { Id = 15, Type = "release", Role = "Main", Title = "Standalone", Format = "CD, Album" }
            });
            Mocker.GetMock<IDiscogsApi>().Setup(a => a.GetRelease(100, It.IsAny<string>())).Returns(Release(100, "CD", "Album"));
            Mocker.GetMock<IDiscogsApi>().Setup(a => a.GetRelease(110, It.IsAny<string>())).Returns(Release(110, "Vinyl", "7\"", "Single"));

            var artist = Subject.GetArtistInfo("discogs:artist:3", 1);

            artist.Albums.Value.Select(a => a.ForeignAlbumId).Should().Equal("discogs:master:10", "discogs:release:15");
        }

        [Test]
        public void a_masters_type_is_read_once()
        {
            Mocker.GetMock<IDiscogsApi>().Setup(a => a.GetArtistOwnReleases(3, It.IsAny<string>())).Returns(new List<DiscogsArtistReleaseResource>
            {
                new() { Id = 10, Type = "master", Role = "Main", Title = "Album", MainRelease = 100 }
            });
            Mocker.GetMock<IDiscogsApi>().Setup(a => a.GetRelease(100, It.IsAny<string>())).Returns(Release(100, "CD", "Album"));

            Subject.GetArtistInfo("discogs:artist:3", 1);
            Subject.GetArtistInfo("discogs:artist:3", 1);

            Mocker.GetMock<IDiscogsApi>().Verify(a => a.GetRelease(100, It.IsAny<string>()), Times.Once());
        }

        [Test]
        public void a_missing_artist_is_not_found()
        {
            Mocker.GetMock<IDiscogsApi>().Setup(a => a.GetArtist(3, It.IsAny<string>())).Returns((DiscogsArtistResource)null);

            Assert.Throws<ArtistNotFoundException>(() => Subject.GetArtistInfo("discogs:artist:3", 1));
        }

        [TestCase(3, new long[] { 100, 102, 101 })]
        [TestCase(0, new long[] { 100, 102, 101, 103 })]
        public void album_info_loads_the_main_release_then_the_most_collected_pressings(int limit, long[] expected)
        {
            _settings.PressingsPerAlbum = limit;
            GivenMaster(10, 100, (101, 5), (102, 50), (103, 1));

            var (artistId, album, _) = Subject.GetAlbumInfo("discogs:master:10");

            artistId.Should().Be("discogs:artist:3");
            album.AlbumReleases.Value.Select(r => r.ForeignReleaseId).Should().Equal(expected.Select(DiscogsMapper.ReleaseId));
            album.AlbumReleases.Value.Single(r => r.Monitored).ForeignReleaseId.Should().Be("discogs:release:100");
        }

        [Test]
        public void one_pressing_per_album_skips_the_version_list()
        {
            _settings.PressingsPerAlbum = 1;
            GivenMaster(10, 100, (101, 5));

            Subject.GetAlbumInfo("discogs:master:10").Item2.AlbumReleases.Value.Should().ContainSingle();

            Mocker.GetMock<IDiscogsApi>().Verify(a => a.GetMasterVersions(It.IsAny<long>(), It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void a_release_without_a_master_is_its_own_album()
        {
            Mocker.GetMock<IDiscogsApi>().Setup(a => a.GetRelease(15, It.IsAny<string>())).Returns(Release(15, "CD", "EP"));

            var album = Subject.GetAlbumInfo("discogs:release:15").Item2;

            album.ForeignAlbumId.Should().Be("discogs:release:15");
            album.AlbumType.Should().Be("EP");
            album.AlbumReleases.Value.Single().Monitored.Should().BeTrue();
        }

        [Test]
        public void searching_by_id_looks_the_artist_up_directly()
        {
            Subject.SearchForNewArtist("discogs:artist:3").Single().Metadata.Value.ForeignArtistId.Should().Be("discogs:artist:3");

            Mocker.GetMock<IDiscogsApi>().Verify(a => a.Search(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void album_search_reads_each_master_for_its_artist()
        {
            GivenMaster(10, 100);
            Mocker.GetMock<IDiscogsApi>().Setup(a => a.Search("master", "Master 10", "Placeholder Artist", It.IsAny<int>(), It.IsAny<string>()))
                .Returns(new List<DiscogsSearchResultResource> { new() { Id = 10, Type = "master", Format = new List<string> { "CD", "Album" } } });

            var album = Subject.SearchForNewAlbum("Master 10", "Placeholder Artist").Single();

            album.ForeignAlbumId.Should().Be("discogs:master:10");
            album.Artist.Value.Metadata.Value.ForeignArtistId.Should().Be("discogs:artist:3");
            album.AlbumReleases.Value.Single().ForeignReleaseId.Should().Be("discogs:release:100");
        }

        [Test]
        public void searching_returns_the_library_artist_when_it_is_already_added()
        {
            var existing = new Artist { Id = 5, Metadata = new ArtistMetadata { ForeignArtistId = "discogs:artist:3", Name = "Placeholder Artist" } };
            Mocker.GetMock<IArtistService>().Setup(s => s.FindById("discogs:artist:3")).Returns(existing);
            Mocker.GetMock<IDiscogsApi>().Setup(a => a.Search("artist", "Placeholder", null, It.IsAny<int>(), It.IsAny<string>()))
                .Returns(new List<DiscogsSearchResultResource> { new() { Id = 3, Type = "artist", Title = "Placeholder Artist", Uri = "/artist/3" } });

            Subject.SearchForNewArtist("Placeholder").Single().Should().BeSameAs(existing);
        }
    }
}
