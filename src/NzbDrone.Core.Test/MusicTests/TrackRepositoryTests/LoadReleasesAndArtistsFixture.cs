using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Music;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MusicTests.TrackRepositoryTests
{
    [TestFixture]
    public class LoadReleasesAndArtistsFixture : DbTest<TrackRepository, Track>
    {
        private int _next;

        [SetUp]
        public void Setup()
        {
            var metadata = Db.Insert(Builder<ArtistMetadata>.CreateNew()
                .With(m => m.ForeignArtistId = Unique("Foreign Artist"))
                .With(m => m.Name = "Artist Name")
                .BuildNew());

            Db.Insert(Builder<Artist>.CreateNew()
                .With(a => a.ArtistMetadataId = metadata.Id)
                .BuildNew());

            var album = Db.Insert(Builder<Album>.CreateNew()
                .With(a => a.ForeignAlbumId = Unique("Foreign Album"))
                .With(a => a.Title = "Album Title")
                .With(a => a.ArtistMetadataId = metadata.Id)
                .BuildNew());

            var release = Db.Insert(Builder<AlbumRelease>.CreateNew()
                .With(r => r.ForeignReleaseId = Unique("Foreign Release"))
                .With(r => r.AlbumId = album.Id)
                .BuildNew());

            GivenTrack(release.Id, metadata.Id);
            GivenTrack(release.Id, metadata.Id);
            GivenTrack(release.Id + 100, metadata.Id);
        }

        private string Unique(string prefix)
        {
            return $"{prefix} {++_next}";
        }

        private void GivenTrack(int albumReleaseId, int artistMetadataId)
        {
            Db.Insert(Builder<Track>.CreateNew()
                .With(t => t.ForeignTrackId = Unique("Foreign Track"))
                .With(t => t.ForeignRecordingId = Unique("Foreign Recording"))
                .With(t => t.AlbumReleaseId = albumReleaseId)
                .With(t => t.ArtistMetadataId = artistMetadataId)
                .BuildNew());
        }

        [Test]
        public void should_load_the_same_releases_albums_and_artists_as_lazy_loading()
        {
            var batched = Subject.All().OrderBy(t => t.Id).ToList();
            var lazy = Subject.All().OrderBy(t => t.Id).ToList();

            Subject.LoadReleasesAndArtists(batched);

            batched.Should().OnlyContain(t => t.AlbumRelease.IsLoaded && t.Artist.IsLoaded);

            for (var i = 0; i < batched.Count; i++)
            {
                (batched[i].AlbumRelease.Value?.Id).Should().Be(lazy[i].AlbumRelease.Value?.Id);
                batched[i].AlbumId.Should().Be(lazy[i].AlbumId);
                (batched[i].Artist.Value?.Id).Should().Be(lazy[i].Artist.Value?.Id);
                (batched[i].Artist.Value?.Metadata.Value.Name).Should().Be(lazy[i].Artist.Value?.Metadata.Value.Name);
            }

            batched.Count(t => t.Artist.Value == null).Should().Be(1);
            batched.Count(t => t.AlbumId > 0).Should().Be(2);
        }

        [Test]
        public void should_keep_releases_and_artists_that_are_already_loaded()
        {
            var tracks = Subject.All().OrderBy(t => t.Id).ToList();
            var release = new AlbumRelease { Id = 12345 };
            var artist = new Artist { Id = 12345 };
            tracks[0].AlbumRelease = release;
            tracks[0].Artist = artist;

            Subject.LoadReleasesAndArtists(tracks);

            tracks[0].AlbumRelease.Value.Should().BeSameAs(release);
            tracks[0].Artist.Value.Should().BeSameAs(artist);
            tracks[1].AlbumRelease.Value.Should().NotBeNull();
            tracks[1].Artist.Value.Should().NotBeNull();
        }
    }
}
