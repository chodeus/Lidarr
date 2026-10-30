using System.Collections.Generic;
using System.IO;
using System.Linq;
using FizzWare.NBuilder;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
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

        private string GivenAlbumFolderInAnArtistFolderWithOtherFiles()
        {
            var file = new TrackFile { Path = Path.Combine(_artist.Path, "Album Title", "01 - Track.flac") };
            _albumDeleted.TrackFilesToDelete = new List<TrackFile> { file };

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FolderExists(_artist.Path))
                  .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.GetFiles(_artist.Path, true))
                  .Returns(new[] { Path.Combine(_artist.Path, "Other Album", "01 - Track.flac") });

            // The folder as the service works it out, separators and all
            return file.Path.GetParentPath();
        }

        [Test]
        public void should_recycle_only_the_files_still_on_disk()
        {
            var onDisk = new TrackFile { Path = Path.Combine(_artist.Path, "Album Title", "01 - Track.flac") };
            var gone = new TrackFile { Path = Path.Combine(_artist.Path, "Album Title", "02 - Track.flac") };
            _albumDeleted.TrackFilesToDelete = new List<TrackFile> { gone, onDisk };

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FileExists(onDisk.Path))
                  .Returns(true);

            Subject.HandleAsync(_albumDeleted);

            Mocker.GetMock<IRecycleBinProvider>()
                  .Verify(s => s.DeleteFile(onDisk.Path, It.IsAny<string>()), Times.Once());
            Mocker.GetMock<IRecycleBinProvider>()
                  .Verify(s => s.DeleteFile(gone.Path, It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_skip_an_album_folder_that_is_not_on_disk()
        {
            var albumFolder = GivenAlbumFolderInAnArtistFolderWithOtherFiles();

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.GetFiles(albumFolder, true))
                  .Throws(new DirectoryNotFoundException());

            Subject.HandleAsync(_albumDeleted);

            Mocker.GetMock<IDiskProvider>()
                  .Verify(s => s.RemoveEmptySubfolders(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_tidy_an_empty_album_folder()
        {
            var albumFolder = GivenAlbumFolderInAnArtistFolderWithOtherFiles();

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FolderExists(albumFolder))
                  .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.GetFiles(albumFolder, true))
                  .Returns(Enumerable.Empty<string>());

            Subject.HandleAsync(_albumDeleted);

            Mocker.GetMock<IDiskProvider>()
                  .Verify(s => s.RemoveEmptySubfolders(albumFolder), Times.Once());
        }
    }
}
