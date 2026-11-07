using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Music;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MusicTests
{
    [TestFixture]
    public class AddAlbumImagesFixture : DbTest<AddAlbumService, Album>
    {
        private const string AlbumId = "11111111-2222-3333-4444-555555555555";
        private const string ArtistId = "66666666-7777-8888-9999-000000000000";

        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<IAlbumRepository>(Mocker.Resolve<AlbumRepository>());
            Mocker.SetConstant<IAlbumService>(Mocker.Resolve<AlbumService>());

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
    }
}
