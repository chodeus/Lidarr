using System;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Music;
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

            Subject.GetAlbumsMissingFromMetadata().Select(a => a.ForeignAlbumId).Should().Equal("missing");
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
            Subject.GetAlbumsMissingFromMetadata().Should().ContainSingle(a => a.Id == album.Id);

            album.MetadataMissingSince = null;
            Subject.SetFields(album, a => a.MetadataMissingSince);
            Subject.GetAlbumsMissingFromMetadata().Should().BeEmpty();
        }
    }
}
