using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnsureThat;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.TrackImport;
using NzbDrone.Core.MediaFiles.TrackImport.Manual;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.TrackImport
{
    // The real ProvideImportItemService and folder-cleanup rules; the download client and the disk are faked
    [TestFixture]
    public class ManualImportExecuteFixture : CoreTest<ManualImportService>
    {
        private Artist _artist;
        private DownloadClientItem _downloadItem;
        private TrackedDownload _trackedDownload;
        private Mock<IDownloadClient> _client;
        private string _outputPath;

        [SetUp]
        public void Setup()
        {
            _artist = Builder<Artist>.CreateNew().With(a => a.Path = "/music/Artist Name".AsOsAgnostic()).Build();
            _outputPath = "/downloads/Artist Name - Album Title".AsOsAgnostic();
            _downloadItem = GivenDownloadItem("download-1", 1, _outputPath);
            _trackedDownload = new TrackedDownload { DownloadItem = _downloadItem, State = TrackedDownloadState.ImportFailed };

            var file = new Mock<IFileInfo>();
            file.SetupGet(f => f.Length).Returns(1000);
            file.SetupGet(f => f.LastWriteTimeUtc).Returns(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

            Mocker.GetMock<IArtistService>().Setup(s => s.GetArtist(It.IsAny<int>())).Returns(_artist);
            Mocker.GetMock<IAlbumService>().Setup(s => s.GetAlbum(It.IsAny<int>())).Returns(Builder<Album>.CreateNew().Build());
            Mocker.GetMock<IReleaseService>().Setup(s => s.GetRelease(It.IsAny<int>())).Returns(Builder<AlbumRelease>.CreateNew().Build());
            Mocker.GetMock<ITrackService>().Setup(s => s.GetTracks(It.IsAny<IEnumerable<int>>())).Returns(Builder<Track>.CreateListOfSize(1).Build() as List<Track>);
            Mocker.GetMock<IAudioTagService>().Setup(s => s.ReadTags(It.IsAny<string>())).Returns(new ParsedTrackInfo());
            Mocker.GetMock<IRootFolderService>().Setup(s => s.GetBestRootFolder(It.IsAny<string>())).Returns(new RootFolder());
            Mocker.GetMock<ITrackedDownloadService>().Setup(s => s.Find("download-1")).Returns(_trackedDownload);
            Mocker.GetMock<IImportApprovedTracks>()
                  .Setup(s => s.Import(It.IsAny<List<ImportDecision<LocalTrack>>>(), It.IsAny<bool>(), It.IsAny<DownloadClientItem>(), It.IsAny<ImportMode>()))
                  .Returns((List<ImportDecision<LocalTrack>> decisions, bool replace, DownloadClientItem item, ImportMode mode) => decisions.ConvertAll(d => new ImportResult(d)));

            Mocker.GetMock<IDiskProvider>().Setup(s => s.GetFileInfo(It.IsAny<string>())).Returns(file.Object);
            Mocker.GetMock<IDiskProvider>().Setup(s => s.GetDirectoryInfo(It.IsAny<string>())).Returns((string path) => Mock.Of<IDirectoryInfo>(d => d.FullName == path));

            // Same guard as DiskProviderBase.FolderExists
            Mocker.GetMock<IDiskProvider>().Setup(s => s.FolderExists(It.IsAny<string>()))
                  .Returns((string path) =>
                  {
                      Ensure.That(path, () => path).IsValidPath(PathValidationType.CurrentOs);
                      return path.StartsWith("/downloads".AsOsAgnostic());
                  });
            Mocker.GetMock<IDiskProvider>().Setup(s => s.GetFiles(It.IsAny<string>(), true)).Returns(Array.Empty<string>());
            Mocker.GetMock<IDiskScanService>().Setup(s => s.GetAudioFiles(It.IsAny<string>(), true)).Returns(Array.Empty<IFileInfo>());

            _client = new Mock<IDownloadClient>();
            _client.Setup(c => c.GetImportItem(It.IsAny<DownloadClientItem>(), It.IsAny<DownloadClientItem>()))
                   .Returns((DownloadClientItem item, DownloadClientItem previous) => item);
            Mocker.GetMock<IProvideDownloadClient>().Setup(s => s.Get(It.IsAny<int>())).Returns(_client.Object);
            Mocker.SetConstant<IProvideImportItemService>(Mocker.Resolve<ProvideImportItemService>());
            Mocker.SetConstant<IDownloadedTracksImportService>(Mocker.Resolve<DownloadedTracksImportService>());
        }

        private static DownloadClientItem GivenDownloadItem(string downloadId, int clientId, string outputPath)
        {
            return new DownloadClientItem
            {
                DownloadId = downloadId,
                Title = downloadId,
                Status = DownloadItemStatus.Completed,
                OutputPath = new OsPath(outputPath),
                CanMoveFiles = true,
                DownloadClientInfo = new DownloadClientItemClientInfo { Id = clientId }
            };
        }

        private static ManualImportFile GivenFile(string downloadId, int albumId)
        {
            return new ManualImportFile
            {
                Path = "/downloads/Artist Name - Album Title/01 - Track Title.flac".AsOsAgnostic(),
                ArtistId = 1,
                AlbumId = albumId,
                AlbumReleaseId = 1,
                TrackIds = new List<int> { 1 },
                Quality = new QualityModel(Quality.FLAC),
                DownloadId = downloadId
            };
        }

        private void Execute(params ManualImportFile[] files)
        {
            Subject.Execute(new ManualImportCommand { ImportMode = ImportMode.Auto, Files = new List<ManualImportFile>(files) });
        }

        private static RemoteAlbum GivenRemoteAlbum(int trackCount)
        {
            var album = new Album { AlbumReleases = new List<AlbumRelease> { new AlbumRelease { Monitored = true, TrackCount = trackCount } } };

            return new RemoteAlbum { Albums = new List<Album> { album } };
        }

        // The real hub acts on the events, as for a client that removes completed downloads with their data
        private void GivenRealDownloadEventHub()
        {
            _downloadItem.CanBeRemoved = true;
            _client.SetupGet(c => c.Definition).Returns(new DownloadClientDefinition { RemoveCompletedDownloads = true });
            Mocker.GetMock<ITrackedDownloadService>().Setup(s => s.GetTrackedDownloads()).Returns(() => new List<TrackedDownload> { _trackedDownload });

            var hub = new DownloadEventHub(Mocker.Resolve<IConfigService>(), Mocker.Resolve<IProvideDownloadClient>(), TestLogger);
            Mocker.GetMock<IEventAggregator>().Setup(s => s.PublishEvent(It.IsAny<DownloadCompletedEvent>())).Callback((DownloadCompletedEvent e) => hub.Handle(e));
            Mocker.GetMock<IEventAggregator>().Setup(s => s.PublishEvent(It.IsAny<DownloadCanBeRemovedEvent>())).Callback((DownloadCanBeRemovedEvent e) => hub.Handle(e));
        }

        // Completed by its client, but never processed by completed-download handling, so it has no import item
        [Test]
        public void should_finish_a_completed_download_without_an_import_item()
        {
            GivenRealDownloadEventHub();

            Execute(GivenFile("download-1", 1));

            Mocker.GetMock<IDiskProvider>().Verify(s => s.DeleteFolder(_outputPath, true), Times.Once());
            _trackedDownload.State.Should().Be(TrackedDownloadState.Imported);
            _client.Verify(c => c.RemoveItem(_downloadItem, true), Times.Once());
        }

        [Test]
        public void should_keep_the_import_item_completed_download_handling_provided()
        {
            var importItem = GivenDownloadItem("download-1", 1, "/downloads/Imported Elsewhere".AsOsAgnostic());
            _trackedDownload.ImportItem = importItem;

            Execute(GivenFile("download-1", 1));

            _client.Verify(c => c.GetImportItem(It.IsAny<DownloadClientItem>(), It.IsAny<DownloadClientItem>()), Times.Never());
            _trackedDownload.ImportItem.Should().BeSameAs(importItem);
        }

        // Partial files don't count as audio, so the cleanup rules alone would delete the folder; usenet clients can remove a paused or queued job
        [TestCase(DownloadItemStatus.Downloading)]
        [TestCase(DownloadItemStatus.Queued)]
        [TestCase(DownloadItemStatus.Paused)]
        [TestCase(DownloadItemStatus.Warning)]
        public void should_leave_a_download_its_client_has_not_completed_alone(DownloadItemStatus status)
        {
            GivenRealDownloadEventHub();
            _downloadItem.Status = status;
            _trackedDownload.State = TrackedDownloadState.Downloading;
            _trackedDownload.RemoteAlbum = GivenRemoteAlbum(1);
            Mocker.GetMock<IDiskProvider>().Setup(s => s.GetFiles(_outputPath, true)).Returns(new[] { (_outputPath + "/02 - Track Two.flac.part").AsOsAgnostic() });

            Execute(GivenFile("download-1", 1));
            Mocker.Resolve<DownloadProcessingService>().Execute(new ProcessMonitoredDownloadsCommand());

            Mocker.GetMock<IDiskProvider>().Verify(s => s.DeleteFolder(It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
            _trackedDownload.State.Should().Be(TrackedDownloadState.Downloading);
            _client.Verify(c => c.RemoveItem(It.IsAny<DownloadClientItem>(), It.IsAny<bool>()), Times.Never());
        }

        // Usenet clients report a queued item with no output path
        [Test]
        public void should_not_throw_for_a_queued_download_without_an_output_path()
        {
            _downloadItem.Status = DownloadItemStatus.Queued;
            _downloadItem.OutputPath = default(OsPath);

            Assert.DoesNotThrow(() => Execute(GivenFile("download-1", 1)));
        }

        [Test]
        public void should_mark_a_completed_download_without_an_output_path_imported_and_leave_its_folder()
        {
            _downloadItem.OutputPath = default(OsPath);

            Execute(GivenFile("download-1", 1));

            Mocker.GetMock<IDiskProvider>().Verify(s => s.DeleteFolder(It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
            _trackedDownload.State.Should().Be(TrackedDownloadState.Imported);
        }

        // A remote path with no mapping isn't a valid local path, and checking it throws
        [Test]
        public void should_mark_a_completed_download_with_an_unmapped_remote_path_imported()
        {
            _downloadItem.OutputPath = new OsPath(OsInfo.IsWindows ? "/downloads/Album Title" : @"C:\downloads\Album Title");

            Execute(GivenFile("download-1", 1));

            Mocker.GetMock<IDiskProvider>().Verify(s => s.FolderExists(It.IsAny<string>()), Times.Never());
            _trackedDownload.State.Should().Be(TrackedDownloadState.Imported);
        }

        [Test]
        public void should_still_finish_other_downloads_when_one_import_item_lookup_fails()
        {
            Mocker.GetMock<IProvideDownloadClient>().Setup(s => s.Get(1)).Throws(new InvalidOperationException("Sequence contains no matching element"));
            var otherItem = GivenDownloadItem("download-2", 2, "/downloads/Other Album".AsOsAgnostic());
            var otherTracked = new TrackedDownload { DownloadItem = otherItem, State = TrackedDownloadState.ImportFailed };
            Mocker.GetMock<ITrackedDownloadService>().Setup(s => s.Find("download-2")).Returns(otherTracked);

            Execute(GivenFile("download-1", 1), GivenFile("download-2", 2));

            ExceptionVerification.ExpectedWarns(1);
            new { First = _trackedDownload.State, Second = otherTracked.State }.Should().BeEquivalentTo(new { First = TrackedDownloadState.Imported, Second = TrackedDownloadState.Imported });
            Mocker.GetMock<IDiskProvider>().Verify(s => s.DeleteFolder("/downloads/Other Album".AsOsAgnostic(), true), Times.Once());
        }

        [Test]
        public void should_import_files_with_an_unknown_download_id()
        {
            Execute(GivenFile("download-unknown", 1));

            Mocker.GetMock<IImportApprovedTracks>()
                  .Verify(s => s.Import(It.IsAny<List<ImportDecision<LocalTrack>>>(), It.IsAny<bool>(), null, It.IsAny<ImportMode>()), Times.Once());
        }
    }
}
