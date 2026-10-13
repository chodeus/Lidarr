using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Music;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MusicTests.AlbumRepositoryTests
{
    [TestFixture]
    public class LoadArtistsAndReleasesFixture : DbTest<AlbumRepository, Album>
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

            var withReleases = GivenAlbum(metadata.Id);
            GivenRelease(withReleases);
            GivenRelease(withReleases);

            GivenAlbum(metadata.Id);
            GivenAlbum(metadata.Id + 100);
        }

        private string Unique(string prefix)
        {
            return $"{prefix} {++_next}";
        }

        private Album GivenAlbum(int artistMetadataId)
        {
            return Db.Insert(Builder<Album>.CreateNew()
                .With(a => a.ForeignAlbumId = Unique("Foreign Album"))
                .With(a => a.Title = "Album Title")
                .With(a => a.ArtistMetadataId = artistMetadataId)
                .BuildNew());
        }

        private void GivenRelease(Album album)
        {
            Db.Insert(Builder<AlbumRelease>.CreateNew()
                .With(r => r.ForeignReleaseId = Unique("Foreign Release"))
                .With(r => r.AlbumId = album.Id)
                .BuildNew());
        }

        [Test]
        public void should_load_the_same_artists_and_releases_as_lazy_loading()
        {
            var batched = Subject.All().OrderBy(a => a.Id).ToList();
            var lazy = Subject.All().OrderBy(a => a.Id).ToList();

            Subject.LoadArtistsAndReleases(batched);

            batched.Should().OnlyContain(a => a.Artist.IsLoaded && a.AlbumReleases.IsLoaded);

            for (var i = 0; i < batched.Count; i++)
            {
                (batched[i].Artist.Value?.Id).Should().Be(lazy[i].Artist.Value?.Id);
                (batched[i].Artist.Value?.Metadata.Value.Name).Should().Be(lazy[i].Artist.Value?.Metadata.Value.Name);
                batched[i].AlbumReleases.Value.Select(r => r.Id).Should().Equal(lazy[i].AlbumReleases.Value.Select(r => r.Id));
            }

            batched.Count(a => a.Artist.Value == null).Should().Be(1);
            batched.Sum(a => a.AlbumReleases.Value.Count).Should().Be(2);
        }

        [Test]
        public void should_keep_artists_and_releases_that_are_already_loaded()
        {
            var albums = Subject.All().OrderBy(a => a.Id).ToList();
            var artist = new Artist { Id = 12345 };
            albums[0].Artist = artist;
            albums[0].AlbumReleases = new List<AlbumRelease>();

            Subject.LoadArtistsAndReleases(albums);

            albums[0].Artist.Value.Should().BeSameAs(artist);
            albums[0].AlbumReleases.Value.Should().BeEmpty();
        }
    }
}
