using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Music;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.NotificationTests.MetadataLinks
{
    [TestFixture]
    public class NotificationMetadataLinkGeneratorFixture : CoreTest
    {
        private static readonly int[] AllLinks = { (int)MetadataLinkType.MusicBrainzArtist, (int)MetadataLinkType.MusicBrainzAlbum };

        private Artist _artist;
        private Album _album;

        [SetUp]
        public void Setup()
        {
            _artist = new Artist { Metadata = new LazyLoaded<ArtistMetadata>(new ArtistMetadata { ForeignArtistId = "artist-mbid" }) };
            _album = new Album { ForeignAlbumId = "album-mbid" };
        }

        [Test]
        public void should_link_the_artist_and_a_single_album()
        {
            var links = NotificationMetadataLinkGenerator.GenerateLinks(_artist, new List<Album> { _album }, AllLinks);

            links.Select(l => l.Link).Should().Equal(
                "https://musicbrainz.org/artist/artist-mbid",
                "https://musicbrainz.org/release-group/album-mbid");
        }

        [Test]
        public void should_not_link_an_album_when_the_message_covers_several()
        {
            var links = NotificationMetadataLinkGenerator.GenerateLinks(_artist, new List<Album> { _album, new Album { ForeignAlbumId = "other" } }, AllLinks);

            links.Should().ContainSingle().Which.Type.Should().Be(MetadataLinkType.MusicBrainzArtist);
        }

        [Test]
        public void should_only_add_the_selected_links()
        {
            var links = NotificationMetadataLinkGenerator.GenerateLinks(_artist, new List<Album> { _album }, new[] { (int)MetadataLinkType.MusicBrainzAlbum });

            links.Should().ContainSingle().Which.Type.Should().Be(MetadataLinkType.MusicBrainzAlbum);
        }

        [Test]
        public void should_return_no_links_without_an_artist()
        {
            NotificationMetadataLinkGenerator.GenerateLinks(null, new List<Album> { _album }, AllLinks).Should().BeEmpty();
        }

        [Test]
        public void should_skip_the_artist_link_without_artist_metadata()
        {
            var links = NotificationMetadataLinkGenerator.GenerateLinks(new Artist(), null, AllLinks);

            links.Should().BeEmpty();
        }
    }
}
