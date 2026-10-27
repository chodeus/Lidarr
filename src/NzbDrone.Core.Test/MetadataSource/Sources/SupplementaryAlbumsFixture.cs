using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MetadataSource.Sources;
using NzbDrone.Core.MetadataSource.Sources.Lidarr;
using NzbDrone.Core.Music;
using NzbDrone.Core.Profiles.MetadataSources;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MetadataSource.Sources
{
    // The real aggregator and MissingAlbums; sources, profiles and the library are mocked
    [TestFixture]
    public class SupplementaryAlbumsFixture : CoreTest<MetadataSourceAggregator>
    {
        private const string MusicBrainzId = "11111111-1111-1111-1111-111111111111";
        private const string OtherLink = "https://other.example.invalid/artist/";

        private MetadataSourceProfile _profile;
        private List<IMetadataSource> _sources;

        [SetUp]
        public void Setup()
        {
            _profile = new MetadataSourceProfile { Id = 1, Name = "Default" };
            _sources = new List<IMetadataSource>();

            Mocker.SetConstant<ICacheManager>(Mocker.Resolve<CacheManager>());

            var lidarr = new Mock<LidarrMetadataSource>(MockBehavior.Default, new object[] { null }) { CallBase = true };
            Mocker.SetConstant(lidarr.Object);

            Mocker.GetMock<IMetadataSourceProfileService>().Setup(s => s.GetDefaultProfile()).Returns(() => _profile);
            Mocker.GetMock<IMetadataSourceFactory>().Setup(s => s.GetAvailableProviders()).Returns(() => _sources.ToList());

            Mocker.GetMock<IArtistService>().Setup(s => s.FindById(MusicBrainzId)).Returns(new Artist { Id = 5 });
            Mocker.GetMock<IAlbumService>().Setup(s => s.GetAlbumsByArtist(5)).Returns(new List<Album>
            {
                NamedAlbum("mb-1", "First Album", 2001),
                NamedAlbum("other:102", "Second Album", 2003)
            });
        }

        private Mock<IMetadataSource> GivenSource(int id, MetadataSourceRole role, string family)
        {
            var source = new Mock<IMetadataSource>();
            source.SetupGet(s => s.Definition).Returns(new MetadataSourceDefinition { Id = id, Name = "Source " + id });
            source.SetupGet(s => s.IdFamily).Returns(family);
            source.Setup(s => s.CanHandleId(It.IsAny<string>()))
                  .Returns<string>(foreignId => family == MetadataIdFamilies.MusicBrainz ? MusicBrainzIds.IsMusicBrainzId(foreignId) : foreignId.StartsWith("other:"));
            source.Setup(s => s.IdFromLink(It.IsAny<string>()))
                  .Returns<string>(url => family != MetadataIdFamilies.MusicBrainz && url.StartsWith(OtherLink) ? string.Concat("other:", url.AsSpan(OtherLink.Length)) : null);

            _sources.Add(source.Object);
            _profile.Items.Add(new MetadataSourceProfileItem { MetadataSourceId = id, Role = role });

            return source;
        }

        private static Album NamedAlbum(string id, string title, int? year)
        {
            return new Album { ForeignAlbumId = id, Title = title, ReleaseDate = year == null ? null : new DateTime(year.Value, 1, 1), OldForeignAlbumIds = new List<string>() };
        }

        private void GivenPrimary(bool withLink)
        {
            var links = withLink ? new List<Links> { new() { Name = "other", Url = OtherLink + "9" } } : new List<Links>();

            GivenSource(1, MetadataSourceRole.Primary, MetadataIdFamilies.MusicBrainz).Setup(s => s.GetArtistInfo(MusicBrainzId, 1)).Returns(new Artist
            {
                Metadata = new ArtistMetadata { ForeignArtistId = MusicBrainzId, Name = "Artist Name", Links = links },
                Albums = new List<Album> { NamedAlbum("mb-1", "First Album", 2001) }
            });
        }

        [Test]
        public void supplement_not_found_keeps_the_albums_it_added_before()
        {
            GivenPrimary(true);
            GivenSource(2, MetadataSourceRole.Supplementary, "Other").Setup(s => s.GetArtistInfo("other:9", 1)).Throws(new ArtistNotFoundException("other:9"));

            Subject.GetArtistInfo(MusicBrainzId, 1).Albums.Value.Select(a => a.ForeignAlbumId).Should().Equal("mb-1", "other:102");
        }

        [Test]
        public void missing_link_keeps_the_albums_the_supplement_added_before()
        {
            GivenPrimary(false);
            GivenSource(2, MetadataSourceRole.Supplementary, "Other");

            Subject.GetArtistInfo(MusicBrainzId, 1).Albums.Value.Select(a => a.ForeignAlbumId).Should().Equal("mb-1", "other:102");
        }

        [Test]
        public void supplement_empty_album_list_keeps_the_albums_it_added_before()
        {
            GivenPrimary(true);
            GivenSource(2, MetadataSourceRole.Supplementary, "Other").Setup(s => s.GetArtistInfo("other:9", 1)).Returns(new Artist { Albums = new List<Album>() });

            Subject.GetArtistInfo(MusicBrainzId, 1).Albums.Value.Select(a => a.ForeignAlbumId).Should().Equal("mb-1", "other:102");
        }

        [Test]
        public void down_supplement_does_not_keep_an_album_the_primary_now_lists_and_merges_it()
        {
            GivenSource(1, MetadataSourceRole.Primary, MetadataIdFamilies.MusicBrainz).Setup(s => s.GetArtistInfo(MusicBrainzId, 1)).Returns(new Artist
            {
                Metadata = new ArtistMetadata { ForeignArtistId = MusicBrainzId, Name = "Artist Name", Links = new List<Links> { new() { Name = "other", Url = OtherLink + "9" } } },
                Albums = new List<Album> { NamedAlbum("mb-1", "First Album", 2001), NamedAlbum("mb-2", "Second Album", 2003) }
            });
            GivenSource(2, MetadataSourceRole.Supplementary, "Other").Setup(s => s.GetArtistInfo("other:9", 1)).Throws(new TimeoutException("Source 2 is down"));

            var albums = Subject.GetArtistInfo(MusicBrainzId, 1).Albums.Value;

            albums.Select(a => a.ForeignAlbumId).Should().Equal("mb-1", "mb-2");
            albums.Last().OldForeignAlbumIds.Should().Contain("other:102");

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void an_album_the_primary_now_lists_becomes_an_old_id_of_the_primarys_album()
        {
            GivenSource(1, MetadataSourceRole.Primary, MetadataIdFamilies.MusicBrainz).Setup(s => s.GetArtistInfo(MusicBrainzId, 1)).Returns(new Artist
            {
                Metadata = new ArtistMetadata { ForeignArtistId = MusicBrainzId, Name = "Artist Name", Links = new List<Links> { new() { Name = "other", Url = OtherLink + "9" } } },
                Albums = new List<Album> { NamedAlbum("mb-1", "First Album", 2001), NamedAlbum("mb-2", "Second Album", 2003) }
            });
            GivenSource(2, MetadataSourceRole.Supplementary, "Other").Setup(s => s.GetArtistInfo("other:9", 1))
                .Returns(new Artist { Albums = new List<Album> { NamedAlbum("other:102", "Second Album", 2003) } });

            var albums = Subject.GetArtistInfo(MusicBrainzId, 1).Albums.Value;

            albums.Select(a => a.ForeignAlbumId).Should().Equal("mb-1", "mb-2");
            albums.Last().OldForeignAlbumIds.Should().Equal("other:102");
        }

        [Test]
        public void supplementary_album_keeps_its_owner_after_the_source_becomes_a_fallback()
        {
            GivenSource(1, MetadataSourceRole.Primary, MetadataIdFamilies.MusicBrainz);
            var sourceArtist = new ArtistMetadata { ForeignArtistId = "other:9", Name = "Artist Name" };
            var album = new Album { ForeignAlbumId = "other:101", ArtistMetadata = sourceArtist, AlbumReleases = new List<AlbumRelease> { new() { Tracks = new List<Track> { new() { ArtistMetadata = sourceArtist } } } } };
            GivenSource(2, MetadataSourceRole.Fallback, "Other").Setup(s => s.GetAlbumInfo("other:101")).Returns(Tuple.Create("other:9", album, new List<ArtistMetadata> { sourceArtist }));

            var owner = new Artist { Id = 5, ArtistMetadataId = 50, Metadata = new ArtistMetadata { Id = 50, ForeignArtistId = MusicBrainzId, Name = "Artist Name" } };
            Mocker.GetMock<IAlbumService>().Setup(s => s.FindById("other:101")).Returns(new Album { ForeignAlbumId = "other:101", ArtistMetadataId = 50 });
            Mocker.GetMock<IArtistService>().Setup(s => s.GetArtistByMetadataId(50)).Returns(owner);

            Subject.GetAlbumInfo("other:101").Item1.Should().Be(MusicBrainzId);
        }

        [Test]
        public void symbol_only_titles_are_different_albums()
        {
            var known = new List<Album> { NamedAlbum("mb-1", "÷", 2017) };
            var candidates = new List<Album> { NamedAlbum("other:1", "=", 2017), NamedAlbum("other:2", "- (Deluxe Edition)", 2023) };

            MissingAlbums.ByIdOrTitle(known, candidates).Select(a => a.ForeignAlbumId).Should().Equal("other:1", "other:2");
        }

        [Test]
        public void supplements_own_same_title_albums_a_year_apart_are_both_added()
        {
            var known = new List<Album> { NamedAlbum("mb-1", "First Album", 2001) };
            var candidates = new List<Album> { NamedAlbum("other:1", "Live", 2004), NamedAlbum("other:2", "Live", 2005) };

            MissingAlbums.ByIdOrTitle(known, candidates).Select(a => a.ForeignAlbumId).Should().Equal("other:1", "other:2");
        }

        [Test]
        public void known_album_without_year_does_not_hide_a_same_title_album_decades_later()
        {
            var known = new List<Album> { NamedAlbum("mb-1", "Greatest Hits", null) };
            var candidates = new List<Album> { NamedAlbum("other:1", "Greatest Hits", 2020) };

            MissingAlbums.ByIdOrTitle(known, candidates).Select(a => a.ForeignAlbumId).Should().Equal("other:1");
        }

        [Test]
        public void case_and_accent_only_differences_match()
        {
            var known = new List<Album> { NamedAlbum("mb-1", "Café Del Mar", 2001) };
            var candidates = new List<Album> { NamedAlbum("other:1", "CAFE DEL MAR", 2001) };

            MissingAlbums.ByIdOrTitle(known, candidates).Should().BeEmpty();
        }

        [Test]
        public void different_non_latin_titles_stay_different()
        {
            var known = new List<Album> { NamedAlbum("mb-1", "東京", 2001) };
            var candidates = new List<Album> { NamedAlbum("other:1", "大阪", 2001), NamedAlbum("other:2", "東京", 2001) };

            MissingAlbums.ByIdOrTitle(known, candidates).Select(a => a.ForeignAlbumId).Should().Equal("other:1");
        }
    }
}
