using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.TrackImport;
using NzbDrone.Core.MediaFiles.TrackImport.Manual;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.TrackImport.Manual
{
    [TestFixture]
    public class ManualImportServiceFixture : CoreTest<ManualImportService>
    {
        private const string DownloadId = "download-1";

        private string _folder;
        private string _file;
        private DownloadClientItem _downloadItem;
        private Mock<IFileInfo> _fileInfo;

        [SetUp]
        public void Setup()
        {
            _folder = @"C:\Test\Downloads\Artist - Album [GROUP]".AsOsAgnostic();
            _file = Path.Combine(_folder, "01 - Track.flac");

            _downloadItem = new DownloadClientItem
            {
                Title = "Artist - Album [GROUP]",
                OutputPath = new OsPath(_folder)
            };

            Mocker.GetMock<ITrackedDownloadService>()
                  .Setup(s => s.Find(DownloadId))
                  .Returns(new TrackedDownload { DownloadItem = _downloadItem, ImportItem = _downloadItem });

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FolderExists(It.IsAny<string>()))
                  .Returns(true);

            _fileInfo = new Mock<IFileInfo>();
            _fileInfo.SetupGet(f => f.FullName).Returns(_file);

            Mocker.GetMock<IDiskScanService>()
                  .Setup(s => s.GetAudioFiles(It.IsAny<string>(), It.IsAny<bool>()))
                  .Returns(new[] { _fileInfo.Object });

            Mocker.GetMock<IMakeImportDecision>()
                  .Setup(s => s.GetImportDecisions(It.IsAny<List<IFileInfo>>(), It.IsAny<IdentificationOverrides>(), It.IsAny<ImportDecisionMakerInfo>(), It.IsAny<ImportDecisionMakerConfig>()))
                  .Returns(new List<ImportDecision<LocalTrack>>());

            Mocker.GetMock<IMakeImportDecision>()
                  .Setup(s => s.GetImportDecisions(It.IsAny<List<IFileInfo>>(), It.IsAny<IdentificationOverrides>(), It.Is<ImportDecisionMakerInfo>(i => i != null && i.DownloadClientItem == _downloadItem), It.IsAny<ImportDecisionMakerConfig>()))
                  .Returns(new List<ImportDecision<LocalTrack>> { new ImportDecision<LocalTrack>(new LocalTrack { Path = _file, ReleaseGroup = "GROUP" }) });
        }

        [Test]
        public void should_return_release_group_for_download_folder()
        {
            var items = Subject.GetMediaFiles(_folder, DownloadId, null, FilterFilesType.None, false);

            items.Should().ContainSingle().Which.ReleaseGroup.Should().Be("GROUP");
        }

        [Test]
        public void should_return_release_group_for_single_file_download()
        {
            _downloadItem.OutputPath = new OsPath(_file);

            Mocker.GetMock<IDiskProvider>().Setup(s => s.FolderExists(_file)).Returns(false);
            Mocker.GetMock<IDiskProvider>().Setup(s => s.FileExists(_file)).Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(s => s.GetFileInfo(_file)).Returns(_fileInfo.Object);

            var items = Subject.GetMediaFiles(_file, DownloadId, null, FilterFilesType.None, false);

            items.Should().ContainSingle().Which.ReleaseGroup.Should().Be("GROUP");
        }
    }
}
