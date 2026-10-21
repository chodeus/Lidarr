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
            _artist = new Artist { Metadata = new LazyLoaded<ArtistMetadata>(new ArtistMetadata { ForeignArtistId = "11111111-1111-1111-1111-111111111111" }) };
            _album = new Album { ForeignAlbumId = "22222222-2222-2222-2222-222222222222" };
        }

        [Test]
        public void should_link_the_artist_and_a_single_album()
        {
            var links = NotificationMetadataLinkGenerator.GenerateLinks(_artist, new List<Album> { _album }, AllLinks);

            links.Select(l => l.Link).Should().Equal(
                "https://musicbrainz.org/artist/11111111-1111-1111-1111-111111111111",
                "https://musicbrainz.org/release-group/22222222-2222-2222-2222-222222222222");
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
        public void should_not_link_another_sources_ids()
        {
            var artist = new Artist { Metadata = new LazyLoaded<ArtistMetadata>(new ArtistMetadata { ForeignArtistId = "other:1" }) };

            NotificationMetadataLinkGenerator.GenerateLinks(artist, new List<Album> { new Album { ForeignAlbumId = "other:2" } }, AllLinks).Should().BeEmpty();
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

        [TestCase(" 11111111-1111-1111-1111-111111111111 ")]
        [TestCase("00000000-0000-0000-0000-000000000000")]
        [TestCase("11111111111111111111111111111111")]
        [TestCase("")]
        public void should_not_link_an_id_that_is_not_a_canonical_musicbrainz_id(string id)
        {
            var artist = new Artist { Metadata = new LazyLoaded<ArtistMetadata>(new ArtistMetadata { ForeignArtistId = id }) };

            NotificationMetadataLinkGenerator.GenerateLinks(artist, new List<Album> { new Album { ForeignAlbumId = id } }, AllLinks).Should().BeEmpty();
        }

        [Test]
        public void should_link_a_type_selected_twice_once()
        {
            var links = NotificationMetadataLinkGenerator.GenerateLinks(_artist, null, new[] { (int)MetadataLinkType.MusicBrainzArtist, (int)MetadataLinkType.MusicBrainzArtist });

            links.Should().ContainSingle();
        }
    }
}
