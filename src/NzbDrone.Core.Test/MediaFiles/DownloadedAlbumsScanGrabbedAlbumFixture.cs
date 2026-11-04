using System.Collections.Generic;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.MediaFiles.TrackImport;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles
{
    // A scan of a known download imports it the way automatic import does.
    [TestFixture]
    public class DownloadedAlbumsScanGrabbedAlbumFixture : FileSystemTest<DownloadedAlbumsCommandService>
    {
        private readonly string _folder = "c:\\drop\\Artist - Single\\".AsOsAgnostic();
        private readonly Artist _artist = new Artist { Id = 1 };
        private readonly Album _album = new Album { Id = 7 };
        private TrackedDownload _trackedDownload;

        [SetUp]
        public void Setup()
        {
            FileSystem.AddDirectory(_folder);

            _trackedDownload = new TrackedDownload
            {
                DownloadItem = new DownloadClientItem { DownloadId = "grab-1" },
                RemoteAlbum = new RemoteAlbum { Artist = _artist },
                State = TrackedDownloadState.ImportPending
            };

            Mocker.GetMock<ITrackedDownloadService>().Setup(s => s.Find("grab-1")).Returns(_trackedDownload);

            Mocker.GetMock<IDownloadedTracksImportService>()
                .Setup(v => v.ProcessPath(It.IsAny<string>(), It.IsAny<ImportMode>(), It.IsAny<Artist>(), It.IsAny<DownloadClientItem>()))
                .Returns(new List<ImportResult>());

            Mocker.GetMock<IDownloadedTracksImportService>()
                .Setup(v => v.ProcessGrabbedPath(It.IsAny<string>(), It.IsAny<ImportMode>(), It.IsAny<Artist>(), It.IsAny<Album>(), It.IsAny<DownloadClientItem>()))
                .Returns(new List<ImportResult>());
        }

        private void Scan() => Subject.Execute(new DownloadedAlbumsScanCommand { Path = _folder, DownloadClientId = "grab-1" });

        [Test]
        public void should_import_a_known_download_against_its_grabbed_album()
        {
            Mocker.GetMock<ICompletedDownloadService>().Setup(s => s.GrabbedAlbum(_trackedDownload)).Returns(_album);

            Scan();

            Mocker.GetMock<IDownloadedTracksImportService>()
                .Verify(v => v.ProcessGrabbedPath(_folder, It.IsAny<ImportMode>(), _artist, _album, _trackedDownload.DownloadItem), Times.Once());
            Mocker.GetMock<IDownloadedTracksImportService>()
                .Verify(v => v.ProcessPath(It.IsAny<string>(), It.IsAny<ImportMode>(), It.IsAny<Artist>(), It.IsAny<DownloadClientItem>()), Times.Never());
        }

        [Test]
        public void should_keep_develops_import_when_the_grab_names_no_single_album()
        {
            Scan();

            Mocker.GetMock<IDownloadedTracksImportService>()
                .Verify(v => v.ProcessPath(_folder, It.IsAny<ImportMode>(), _artist, _trackedDownload.DownloadItem), Times.Once());
            Mocker.GetMock<IDownloadedTracksImportService>()
                .Verify(v => v.ProcessGrabbedPath(It.IsAny<string>(), It.IsAny<ImportMode>(), It.IsAny<Artist>(), It.IsAny<Album>(), It.IsAny<DownloadClientItem>()), Times.Never());
        }
    }
}
