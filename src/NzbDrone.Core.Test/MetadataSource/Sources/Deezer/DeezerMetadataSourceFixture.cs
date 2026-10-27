using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MetadataSource.Sources.Deezer;
using NzbDrone.Core.Music;
using NzbDrone.Core.Profiles.Metadata;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource.Sources.Deezer
{
    [TestFixture]
    public class DeezerMetadataSourceFixture : CoreTest<DeezerMetadataSource>
    {
        private static readonly DeezerArtistResource MainArtist = new() { Id = 10, Name = "Placeholder Artist", Link = "https://www.deezer.com/artist/10" };
        private static readonly DeezerArtistResource GuestArtist = new() { Id = 11, Name = "Placeholder Guest" };

        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IMetadataProfileService>().Setup(s => s.Exists(1)).Returns(true);
            Mocker.GetMock<IMetadataProfileService>().Setup(s => s.Get(1)).Returns(MetadataProfileAlbumFilterFixture.AlbumsOnlyProfile());
        }

        private static DeezerAlbumResource Album(long id, string recordType)
        {
            return new DeezerAlbumResource { Id = id, Title = "Album " + id, RecordType = recordType, ReleaseDate = "2001-02-03", Artist = MainArtist, Label = "Placeholder Label", TrackCount = 2 };
        }

        private static DeezerTrackResource Track(long id, int disk, int position, DeezerArtistResource artist)
        {
            return new DeezerTrackResource { Id = id, Title = "Track " + id, Duration = 200, DiskNumber = disk, TrackPosition = position, Artist = artist };
        }

        [TestCase("deezer:10", true)]
        [TestCase("Deezer:10", true)]
        [TestCase("deezer:abc", false)]
        [TestCase("deezer:-1", false)]
        [TestCase("10", false)]
        [TestCase("11111111-1111-1111-1111-111111111111", false)]
        public void handles_only_deezer_ids(string foreignId, bool expected)
        {
            Subject.CanHandleId(foreignId).Should().Be(expected);
        }

        [Test]
        public void artist_info_keeps_only_the_albums_the_metadata_profile_allows()
        {
            Mocker.GetMock<IDeezerApi>().Setup(a => a.GetArtist(10)).Returns(MainArtist);
            Mocker.GetMock<IDeezerApi>().Setup(a => a.GetArtistAlbums(10)).Returns(new List<DeezerAlbumResource> { Album(1, "album"), Album(2, "single"), Album(3, "compile") });

            var artist = Subject.GetArtistInfo("deezer:10", 1);

            artist.Metadata.Value.ForeignArtistId.Should().Be("deezer:10");
            artist.Metadata.Value.Links.Should().ContainSingle(l => l.Name == "Deezer" && l.Url == MainArtist.Link);
            artist.Albums.Value.Select(a => a.ForeignAlbumId).Should().Equal("deezer:1");
        }

        [Test]
        public void a_missing_artist_is_not_found()
        {
            Mocker.GetMock<IDeezerApi>().Setup(a => a.GetArtist(10)).Returns((DeezerArtistResource)null);

            Assert.Throws<ArtistNotFoundException>(() => Subject.GetArtistInfo("deezer:10", 1));
        }

        [Test]
        public void album_info_numbers_the_tracks_across_discs()
        {
            Mocker.GetMock<IDeezerApi>().Setup(a => a.GetAlbum(1)).Returns(Album(1, "album"));
            Mocker.GetMock<IDeezerApi>().Setup(a => a.GetAlbumTracks(1)).Returns(new List<DeezerTrackResource>
            {
                Track(102, 2, 1, MainArtist),
                Track(101, 1, 2, GuestArtist),
                Track(100, 1, 1, MainArtist)
            });

            var (artistId, album, artists) = Subject.GetAlbumInfo("deezer:1");

            artistId.Should().Be("deezer:10");
            artists.Select(a => a.ForeignArtistId).Should().BeEquivalentTo("deezer:10", "deezer:11");
            album.ReleaseDate.Should().Be(new DateTime(2001, 2, 3));

            var release = album.AlbumReleases.Value.Single();
            release.Monitored.Should().BeTrue();
            release.Media.Select(m => m.Number).Should().Equal(1, 2);
            release.Tracks.Value.Select(t => (t.ForeignTrackId, t.MediumNumber, t.TrackNumber, t.AbsoluteTrackNumber)).Should().Equal(
                ("deezer:100", 1, "1", 1),
                ("deezer:101", 1, "2", 2),
                ("deezer:102", 2, "1", 3));
            release.Tracks.Value.Should().OnlyContain(t => t.Duration == 200000);
            release.Tracks.Value[1].ArtistMetadata.Value.ForeignArtistId.Should().Be("deezer:11");
        }

        [Test]
        public void a_compilation_is_an_album_with_the_compilation_type()
        {
            Mocker.GetMock<IDeezerApi>().Setup(a => a.GetAlbum(3)).Returns(Album(3, "compile"));
            Mocker.GetMock<IDeezerApi>().Setup(a => a.GetAlbumTracks(3)).Returns(new List<DeezerTrackResource> { Track(300, 1, 1, MainArtist), Track(301, 1, 2, MainArtist) });

            var album = Subject.GetAlbumInfo("deezer:3").Item2;

            album.AlbumType.Should().Be("Album");
            album.SecondaryTypes.Should().ContainSingle(t => t.Name == "Compilation");
        }

        [Test]
        public void searching_by_id_looks_the_artist_up_directly()
        {
            Mocker.GetMock<IDeezerApi>().Setup(a => a.GetArtist(10)).Returns(MainArtist);

            Subject.SearchForNewArtist("deezer:10").Single().Metadata.Value.Name.Should().Be("Placeholder Artist");

            Mocker.GetMock<IDeezerApi>().Verify(a => a.SearchArtists(It.IsAny<string>(), It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void searching_returns_the_library_artist_when_it_is_already_added()
        {
            var existing = new Artist { Id = 5, Metadata = new ArtistMetadata { ForeignArtistId = "deezer:10", Name = "Placeholder Artist" } };
            Mocker.GetMock<IArtistService>().Setup(s => s.FindById("deezer:10")).Returns(existing);
            Mocker.GetMock<IDeezerApi>().Setup(a => a.SearchArtists("Placeholder", It.IsAny<int>())).Returns(new List<DeezerArtistResource> { MainArtist });

            Subject.SearchForNewArtist("Placeholder").Single().Should().BeSameAs(existing);
        }

        [Test]
        public void album_search_matches_artist_and_title_separately_and_reads_each_result()
        {
            Mocker.GetMock<IDeezerApi>().Setup(a => a.SearchAlbums("artist:\"Placeholder Artist\" album:\"Album 1\"", It.IsAny<int>())).Returns(new List<DeezerAlbumResource> { new() { Id = 1 } });
            Mocker.GetMock<IDeezerApi>().Setup(a => a.GetAlbum(1)).Returns(Album(1, "album"));

            var album = Subject.SearchForNewAlbum("Album 1", "Placeholder Artist").Single();

            album.ReleaseDate.Should().Be(new DateTime(2001, 2, 3));
            album.Artist.Value.Metadata.Value.ForeignArtistId.Should().Be("deezer:10");
            album.AlbumReleases.Value.Single().TrackCount.Should().Be(2);
        }
    }
}
