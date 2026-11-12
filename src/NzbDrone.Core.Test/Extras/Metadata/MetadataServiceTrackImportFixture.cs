using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Extras.Metadata;
using NzbDrone.Core.Extras.Metadata.Files;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Music;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Extras.Metadata
{
    [TestFixture]
    public class MetadataServiceTrackImportFixture : CoreTest<MetadataService>
    {
        private Mock<IMetadata> Consumer(string name, Func<MetadataFileResult> result)
        {
            var consumer = new Mock<IMetadata>();
            consumer.SetupGet(c => c.Name).Returns(name);
            consumer.Setup(c => c.TrackMetadata(It.IsAny<Artist>(), It.IsAny<TrackFile>())).Returns(result);
            return consumer;
        }

        [Test]
        public void should_keep_a_consumers_row_when_a_later_consumer_throws()
        {
            var writer = Consumer("Writer", () => new MetadataFileResult(Path.Combine("Album", "01 - Song.nfo"), "track"));
            var converter = Consumer("Converter", () => throw new IOException("ffmpeg missing"));
            Mocker.GetMock<IMetadataFactory>().Setup(f => f.Enabled()).Returns(new List<IMetadata> { writer.Object, converter.Object });

            var artist = new Artist { Id = 1, Path = Path.Combine(TempFolder, "Artist") };
            var files = Subject.CreateAfterTrackImport(artist, new TrackFile { AlbumId = 2, Path = Path.Combine(artist.Path, "Album", "01 - Song.flac") }).ToList();

            files.Should().ContainSingle(f => f.RelativePath == Path.Combine("Album", "01 - Song.nfo"));
            Mocker.GetMock<IMetadataFileService>().Verify(s => s.Upsert(It.Is<List<MetadataFile>>(l => l.Count == 1)), Times.Once());
            ExceptionVerification.ExpectedWarns(1);
        }
    }
}
