using System;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Music;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MusicTests.AlbumRepositoryTests
{
    [TestFixture]
    public class AlbumsMissingFromMetadataFixture : DbTest<AlbumRepository, Album>
    {
        [Test]
        public void should_return_only_albums_marked_missing_from_metadata()
        {
            var albums = Builder<Album>.CreateListOfSize(3)
                .All()
                .With(a => a.Id = 0)
                .With(a => a.Title = "Album Title")
                .With(a => a.MetadataMissingSince = null)
                .TheFirst(1)
                .With(a => a.ForeignAlbumId = "missing")
                .With(a => a.MetadataMissingSince = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc))
                .TheNext(1)
                .With(a => a.ForeignAlbumId = "listed-1")
                .TheNext(1)
                .With(a => a.ForeignAlbumId = "listed-2")
                .BuildListOfNew();
            Db.InsertMany(albums);

            Subject.GetAlbumsMissingFromMetadataWithoutFiles().Select(a => a.ForeignAlbumId).Should().Equal("missing");
        }

        [Test]
        public void should_leave_out_albums_with_files()
        {
            var albums = Builder<Album>.CreateListOfSize(2)
                .All()
                .With(a => a.Id = 0)
                .With(a => a.MetadataMissingSince = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc))
                .TheFirst(1)
                .With(a => a.ForeignAlbumId = "without-files")
                .TheNext(1)
                .With(a => a.ForeignAlbumId = "with-files")
                .BuildListOfNew();
            Db.InsertMany(albums);

            var files = Builder<TrackFile>.CreateListOfSize(2)
                .All()
                .With(f => f.Id = 0)
                .With(f => f.Quality = new QualityModel())
                .With(f => f.AlbumId = albums[1].Id)
                .BuildListOfNew();
            Db.InsertMany(files);

            Subject.GetAlbumsMissingFromMetadataWithoutFiles().Select(a => a.ForeignAlbumId).Should().Equal("without-files");
        }

        [Test]
        public void should_load_each_album_with_its_artist_name()
        {
            var metadata = Db.Insert(Builder<ArtistMetadata>.CreateNew()
                .With(m => m.Id = 0)
                .With(m => m.Name = "Artist Name")
                .BuildNew());
            Db.Insert(Builder<Album>.CreateNew()
                .With(a => a.Id = 0)
                .With(a => a.ArtistMetadataId = metadata.Id)
                .With(a => a.MetadataMissingSince = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc))
                .BuildNew());

            var album = Subject.GetAlbumsMissingFromMetadataWithoutFiles().Single();

            album.ArtistMetadata.IsLoaded.Should().BeTrue();
            album.ArtistMetadata.Value.Name.Should().Be("Artist Name");
        }

        [Test]
        public void should_save_and_clear_the_missing_date()
        {
            var album = Db.Insert(Builder<Album>.CreateNew()
                .With(a => a.Id = 0)
                .With(a => a.ForeignAlbumId = "album-1")
                .With(a => a.MetadataMissingSince = null)
                .BuildNew());

            album.MetadataMissingSince = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            Subject.SetFields(album, a => a.MetadataMissingSince);
            Subject.GetAlbumsMissingFromMetadataWithoutFiles().Should().ContainSingle(a => a.Id == album.Id);

            album.MetadataMissingSince = null;
            Subject.SetFields(album, a => a.MetadataMissingSince);
            Subject.GetAlbumsMissingFromMetadataWithoutFiles().Should().BeEmpty();
        }
    }
}
