using System;
using System.Collections.Generic;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.TrackImport;
using NzbDrone.Core.Music;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.TrackImport
{
    [TestFixture]
    public class TrackFileReplacementServiceFixture : CoreTest<TrackFileReplacementService>
    {
        [Test]
        public void should_remove_a_put_back_row_whose_tracks_could_not_be_linked()
        {
            var row = new TrackFile { Id = 9 };
            Mocker.GetMock<IMediaFileService>().Setup(s => s.Add(It.IsAny<TrackFile>())).Returns(row);
            Mocker.GetMock<ITrackService>()
                  .Setup(s => s.SetFileIds(It.IsAny<List<Track>>()))
                  .Throws(new InvalidOperationException("database is locked"));

            Assert.Throws<InvalidOperationException>(() => Subject.PutBack(new TrackFile { Id = 7, AlbumId = 1, Tracks = new List<Track> { new Track { Id = 3 } } }));

            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(row, DeleteMediaFileReason.ManualOverride), Times.Once());
        }
    }
}
