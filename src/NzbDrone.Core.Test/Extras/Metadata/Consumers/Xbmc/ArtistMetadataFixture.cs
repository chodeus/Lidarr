using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Extras.Metadata;
using NzbDrone.Core.Extras.Metadata.Consumers.Xbmc;
using NzbDrone.Core.Music;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Extras.Metadata.Consumers.Xbmc
{
    [TestFixture]
    public class ArtistMetadataFixture : CoreTest<XbmcMetadata>
    {
        [SetUp]
        public void Setup()
        {
            Subject.Definition = new MetadataDefinition { Settings = new XbmcMetadataSettings { ArtistMetadata = true } };
        }

        [TestCase("11111111-1111-1111-1111-111111111111", true)]
        [TestCase("other:1", false)]
        public void artist_nfo_carries_only_a_musicbrainz_artist_id(string foreignArtistId, bool expected)
        {
            var artist = new Artist { Metadata = new ArtistMetadata { ForeignArtistId = foreignArtistId, Name = "Placeholder Artist" } };

            Subject.ArtistMetadata(artist).Contents.Contains("<musicbrainzartistid>").Should().Be(expected);
        }
    }
}
