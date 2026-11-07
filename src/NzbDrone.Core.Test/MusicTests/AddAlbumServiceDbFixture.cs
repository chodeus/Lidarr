using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.ImportLists.Exclusions;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Music;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MusicTests
{
    [TestFixture]
    public class AddAlbumServiceDbFixture : DbTest<AddAlbumService, Album>
    {
        private const string AlbumId = "11111111-2222-3333-4444-555555555555";
        private const string ArtistId = "66666666-7777-8888-9999-000000000000";

        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<IAlbumRepository>(Mocker.Resolve<AlbumRepository>());
            Mocker.SetConstant<IAlbumService>(Mocker.Resolve<AlbumService>());
            Mocker.SetConstant<IImportListExclusionRepository>(Mocker.Resolve<ImportListExclusionRepository>());
            Mocker.SetConstant<IImportListExclusionService>(Mocker.Resolve<ImportListExclusionService>());

            Mocker.GetMock<IArtistService>()
                  .Setup(s => s.FindById(ArtistId))
                  .Returns(new Artist { Id = 1, ArtistMetadataId = 1 });
        }

        private void GivenSourceAlbum(List<MediaCover.MediaCover> images)
        {
            var source = new Album
            {
                ForeignAlbumId = AlbumId,
                Title = "Album Title",
                CleanTitle = "albumtitle",
                AlbumType = "Album",
                Images = images
            };

            Mocker.GetMock<IProvideAlbumInfo>()
                  .Setup(s => s.GetAlbumInfo(AlbumId))
                  .Returns(Tuple.Create(ArtistId, source, new List<ArtistMetadata> { new ArtistMetadata { ForeignArtistId = ArtistId } }));
        }

        private void GivenExclusions()
        {
            var exclusions = Mocker.Resolve<IImportListExclusionService>();

            exclusions.Add(new ImportListExclusion { ForeignId = AlbumId, Name = "Album Title" });
            exclusions.Add(new ImportListExclusion { ForeignId = ArtistId, Name = "Artist Name" });
        }

        // What AlbumResource.ToModel builds from a POST body that has no images
        private static Album PostedWithoutImages()
        {
            return new Album
            {
                ForeignAlbumId = AlbumId,
                Images = null,
                ArtistMetadata = new ArtistMetadata { ForeignArtistId = ArtistId }
            };
        }

        [Test]
        public void should_save_an_album_posted_without_images_whose_source_has_none()
        {
            GivenSourceAlbum(new List<MediaCover.MediaCover>());

            Subject.AddAlbum(PostedWithoutImages(), false);

            Db.All<Album>().Single().Images.Should().BeEmpty();
        }

        [Test]
        public void should_save_the_source_images_for_an_album_posted_without_images()
        {
            GivenSourceAlbum(new List<MediaCover.MediaCover> { new MediaCover.MediaCover(MediaCover.MediaCoverTypes.Cover, "http://example.test/cover.jpg") });

            Subject.AddAlbum(PostedWithoutImages(), false);

            Db.All<Album>().Single().Images.Should().ContainSingle().Which.RemoteUrl.Should().Be("http://example.test/cover.jpg");
        }

        [Test]
        public void should_remove_the_exclusions_of_an_added_album()
        {
            GivenSourceAlbum(new List<MediaCover.MediaCover>());
            GivenExclusions();

            Subject.AddAlbum(PostedWithoutImages(), false);

            Mocker.Resolve<IImportListExclusionService>().All().Should().BeEmpty();
        }

        // Two adds of the same album can both pass the API's already-added check
        [Test]
        public void should_keep_the_exclusions_when_the_add_fails()
        {
            GivenSourceAlbum(new List<MediaCover.MediaCover>());
            Subject.AddAlbum(PostedWithoutImages(), false);
            GivenExclusions();

            Assert.Throws<SQLiteException>(() => Subject.AddAlbum(PostedWithoutImages(), false));

            Mocker.Resolve<IImportListExclusionService>().All().Select(e => e.ForeignId).Should().BeEquivalentTo(AlbumId, ArtistId);
        }
    }
}
