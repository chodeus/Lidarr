using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.TrackImport;
using NzbDrone.Core.MediaFiles.TrackImport.Manual;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.TrackImport
{
    [TestFixture]
    public class ManualImportExecuteFixture : CoreTest<ManualImportService>
    {
        private Artist _artist;
        private DownloadClientItem _downloadItem;
        private TrackedDownload _trackedDownload;
        private string _outputPath;

        [SetUp]
        public void Setup()
        {
            _artist = Builder<Artist>.CreateNew().With(a => a.Path = "/music/Artist Name".AsOsAgnostic()).Build();
            _outputPath = "/downloads/Artist Name - Album Title".AsOsAgnostic();
            _downloadItem = new DownloadClientItem { DownloadId = "download-1", OutputPath = new OsPath(_outputPath), CanMoveFiles = true };
            _trackedDownload = new TrackedDownload { DownloadItem = _downloadItem, State = TrackedDownloadState.ImportFailed };

            var file = new Mock<IFileInfo>();
            file.SetupGet(f => f.Length).Returns(1000);
            file.SetupGet(f => f.LastWriteTimeUtc).Returns(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

            Mocker.GetMock<IArtistService>().Setup(s => s.GetArtist(It.IsAny<int>())).Returns(_artist);
            Mocker.GetMock<IAlbumService>().Setup(s => s.GetAlbum(It.IsAny<int>())).Returns(Builder<Album>.CreateNew().Build());
            Mocker.GetMock<IReleaseService>().Setup(s => s.GetRelease(It.IsAny<int>())).Returns(Builder<AlbumRelease>.CreateNew().Build());
            Mocker.GetMock<ITrackService>().Setup(s => s.GetTracks(It.IsAny<IEnumerable<int>>())).Returns(Builder<Track>.CreateListOfSize(1).Build() as List<Track>);
            Mocker.GetMock<IAudioTagService>().Setup(s => s.ReadTags(It.IsAny<string>())).Returns(new ParsedTrackInfo());
            Mocker.GetMock<IDiskProvider>().Setup(s => s.GetFileInfo(It.IsAny<string>())).Returns(file.Object);
            Mocker.GetMock<IDiskProvider>().Setup(s => s.FolderExists(_outputPath)).Returns(true);
            Mocker.GetMock<IRootFolderService>().Setup(s => s.GetBestRootFolder(It.IsAny<string>())).Returns(new RootFolder());
            Mocker.GetMock<ITrackedDownloadService>().Setup(s => s.Find("download-1")).Returns(_trackedDownload);
            Mocker.GetMock<IImportApprovedTracks>()
                  .Setup(s => s.Import(It.IsAny<List<ImportDecision<LocalTrack>>>(), It.IsAny<bool>(), It.IsAny<DownloadClientItem>(), It.IsAny<ImportMode>()))
                  .Returns((List<ImportDecision<LocalTrack>> decisions, bool replace, DownloadClientItem item, ImportMode mode) => decisions.ConvertAll(d => new ImportResult(d)));
            Mocker.GetMock<IProvideImportItemService>().Setup(s => s.ProvideImportItem(_downloadItem, It.IsAny<DownloadClientItem>())).Returns(_downloadItem);
            Mocker.GetMock<IDownloadedTracksImportService>().Setup(s => s.ShouldDeleteFolder(It.IsAny<IDirectoryInfo>(), It.IsAny<Artist>())).Returns(true);
        }

        private void Execute()
        {
            Subject.Execute(new ManualImportCommand
            {
                ImportMode = ImportMode.Auto,
                Files = new List<ManualImportFile>
                {
                    new ManualImportFile
                    {
                        Path = (_outputPath + "/01 - Track Title.flac").AsOsAgnostic(),
                        ArtistId = 1,
                        AlbumId = 1,
                        AlbumReleaseId = 1,
                        TrackIds = new List<int> { 1 },
                        Quality = new QualityModel(Quality.FLAC),
                        DownloadId = "download-1"
                    }
                }
            });
        }

        // A download that completed-download handling never processed, such as one its client still reports as downloading
        [Test]
        public void should_finish_the_import_of_a_download_without_an_import_item()
        {
            Execute();

            Mocker.GetMock<IDiskProvider>().Verify(s => s.DeleteFolder(_outputPath, true), Times.Once());
            _trackedDownload.State.Should().Be(TrackedDownloadState.Imported);
        }

        [Test]
        public void should_keep_the_import_item_completed_download_handling_provided()
        {
            var importItem = new DownloadClientItem { DownloadId = "download-1", OutputPath = new OsPath("/imports/Artist Name - Album Title".AsOsAgnostic()), CanMoveFiles = true };
            _trackedDownload.ImportItem = importItem;

            Execute();

            Mocker.GetMock<IProvideImportItemService>().Verify(s => s.ProvideImportItem(It.IsAny<DownloadClientItem>(), It.IsAny<DownloadClientItem>()), Times.Never());
            _trackedDownload.ImportItem.Should().BeSameAs(importItem);
        }
    }
}
