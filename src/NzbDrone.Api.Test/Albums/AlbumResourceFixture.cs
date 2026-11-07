using System.Collections.Generic;
using FluentAssertions;
using Lidarr.Api.V1.Albums;
using NUnit.Framework;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.MediaCover;

namespace NzbDrone.Api.Test.Albums
{
    [TestFixture]
    public class AlbumResourceFixture
    {
        [TestCase("{\"foreignAlbumId\": \"11111111-2222-3333-4444-555555555555\", \"title\": \"Album Title\"}")]
        [TestCase("{\"foreignAlbumId\": \"11111111-2222-3333-4444-555555555555\", \"title\": \"Album Title\", \"images\": null}")]
        public void album_posted_without_images_should_get_an_empty_list(string body)
        {
            var album = STJson.Deserialize<AlbumResource>(body).ToModel();

            album.Images.Should().NotBeNull();
            album.Images.Should().BeEmpty();
        }

        [Test]
        public void album_posted_with_images_should_keep_them()
        {
            var resource = new AlbumResource
            {
                Images = new List<MediaCover> { new MediaCover(MediaCoverTypes.Cover, "http://dummy.com/cover.jpg") }
            };

            resource.ToModel().Images.Should().ContainSingle().Which.RemoteUrl.Should().Be("http://dummy.com/cover.jpg");
        }
    }
}
