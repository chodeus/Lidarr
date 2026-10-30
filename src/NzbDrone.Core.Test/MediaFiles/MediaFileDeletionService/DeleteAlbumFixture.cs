using System.Collections.Generic;
using System.IO;
using System.Linq;
using FizzWare.NBuilder;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Music;
using NzbDrone.Core.Music.Events;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.MediaFileDeletionService
{
    [TestFixture]
    public class DeleteAlbumFixture : CoreTest<Core.MediaFiles.MediaFileDeletionService>
    {
        private Artist _artist;
        private AlbumDeletedEvent _albumDeleted;

        [SetUp]
        public void Setup()
        {
            _artist = Builder<Artist>.CreateNew()
                                     .With(s => s.Path = Path.Combine(@"C:\Test\Music", "Artist Name"))
                                     .Build();

            var album = Builder<Album>.CreateNew()
                                      .With(a => a.Artist = _artist)
                                      .Build();

            _albumDeleted = new AlbumDeletedEvent(album, true, false) { TrackFilesToDelete = new List<TrackFile>() };

            Mocker.GetMock<IConfigService>()
                  .SetupGet(s => s.DeleteEmptyFolders)
                  .Returns(true);

            // As the real disk does when asked for the files in a folder that isn't there
            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.GetFiles(_artist.Path, true))
                  .Throws(new DirectoryNotFoundException());
        }

        [Test]
        public void should_skip_an_artist_folder_that_is_not_on_disk()
        {
            Subject.HandleAsync(_albumDeleted);

            Mocker.GetMock<IDiskProvider>()
                  .Verify(s => s.DeleteFolder(_artist.Path, true), Times.Never());
        }

        [Test]
        public void should_delete_an_empty_artist_folder()
        {
            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FolderExists(_artist.Path))
                  .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.GetFiles(_artist.Path, true))
                  .Returns(Enumerable.Empty<string>());

            Subject.HandleAsync(_albumDeleted);

            Mocker.GetMock<IDiskProvider>()
                  .Verify(s => s.DeleteFolder(_artist.Path, true), Times.Once());
        }
    }
}
