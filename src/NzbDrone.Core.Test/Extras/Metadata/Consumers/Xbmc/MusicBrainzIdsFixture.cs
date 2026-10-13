using System;
using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Extras.Metadata;
using NzbDrone.Core.Extras.Metadata.Consumers.Xbmc;
using NzbDrone.Core.Music;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Extras.Metadata.Consumers.Xbmc
{
    [TestFixture]
    public class MusicBrainzIdsFixture : CoreTest<XbmcMetadata>
    {
        [SetUp]
        public void Setup()
        {
            Subject.Definition = new MetadataDefinition { Settings = new XbmcMetadataSettings { ArtistMetadata = true, AlbumMetadata = true } };
        }

        [TestCase("11111111-1111-1111-1111-111111111111", true)]
        [TestCase("other:1", false)]
        public void artist_nfo_carries_only_a_musicbrainz_artist_id(string foreignArtistId, bool expected)
        {
            var artist = new Artist { Metadata = new ArtistMetadata { ForeignArtistId = foreignArtistId, Name = "Placeholder Artist" } };

            Subject.ArtistMetadata(artist).Contents.Contains("<musicbrainzartistid>").Should().Be(expected);
        }

        [TestCase("11111111-1111-1111-1111-111111111111", true)]
        [TestCase("other:1", false)]
        public void album_nfo_carries_only_musicbrainz_album_ids(string foreignAlbumId, bool expected)
        {
            var artist = new Artist { Metadata = new ArtistMetadata { ForeignArtistId = "other:2", Name = "Placeholder Artist" } };
            var album = new Album
            {
                ForeignAlbumId = foreignAlbumId,
                Title = "Placeholder Album",
                ReleaseDate = new DateTime(2001, 2, 3),
                AlbumReleases = new List<AlbumRelease> { new() { ForeignReleaseId = foreignAlbumId, Monitored = true, Label = new List<string>() } }
            };

            var nfo = Subject.AlbumMetadata(artist, album, "/music/album").Contents;

            nfo.Contains("<musicbrainzreleasegroupid>").Should().Be(expected);
            nfo.Contains("<musicbrainzalbumid>").Should().Be(expected);
        }
    }
}
