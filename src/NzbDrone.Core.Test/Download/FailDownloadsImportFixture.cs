using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Disk;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.Newznab;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.TrackImport;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download
{
    // The real import, rejection and indexer settings services; the disk, indexers, history and track matching are mocked
    [TestFixture]
    public class FailDownloadsImportFixture : CoreTest<CompletedDownloadService>
    {
        private TrackedDownload _trackedDownload;

        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<ICacheManager>(new CacheManager());
            Mocker.SetConstant<ICachedIndexerSettingsProvider>(Mocker.Resolve<CachedIndexerSettingsProvider>());
            Mocker.SetConstant<IRejectedImportService>(Mocker.Resolve<RejectedImportService>());
            Mocker.SetConstant<IDownloadedTracksImportService>(Mocker.Resolve<DownloadedTracksImportService>());

            Mocker.GetMock<IHistoryService>()
                  .Setup(s => s.FindByDownloadId(It.IsAny<string>()))
                  .Returns(new List<EntityHistory>());

            Mocker.GetMock<IProvideImportItemService>()
                  .Setup(s => s.ProvideImportItem(It.IsAny<DownloadClientItem>(), It.IsAny<DownloadClientItem>()))
                  .Returns<DownloadClientItem, DownloadClientItem>((i, p) => i);

            Mocker.GetMock<IDiskScanService>()
                  .Setup(s => s.GetAudioFiles(It.IsAny<string>(), It.IsAny<bool>()))
                  .Returns(System.Array.Empty<IFileInfo>());

            Mocker.GetMock<IDiskScanService>()
                  .Setup(s => s.FilterFiles(It.IsAny<string>(), It.IsAny<IEnumerable<IFileInfo>>()))
                  .Returns<string, IEnumerable<IFileInfo>>((b, s) => s.ToList());
        }

        private void GivenDownload(string outputPath)
        {
            var item = Builder<DownloadClientItem>.CreateNew()
                                                  .With(h => h.Status = DownloadItemStatus.Completed)
                                                  .With(h => h.OutputPath = new OsPath(outputPath))
                                                  .With(h => h.Title = "Artist - Album")
                                                  .Build();

            _trackedDownload = new TrackedDownload
            {
                State = TrackedDownloadState.ImportPending,
                DownloadItem = item,
                RemoteAlbum = new RemoteAlbum
                {
                    Artist = new Artist(),
                    Albums = new List<Album> { new Album { Id = 1, AlbumReleases = new List<AlbumRelease> { new AlbumRelease { Monitored = true, TrackCount = 10 } } } },
                    Release = new ReleaseInfo { IndexerId = 4, Title = "Artist - Album" }
                }
            };
        }

        private void GivenIndexerFails(params FailDownloads[] failDownloads)
        {
            Mocker.GetMock<IIndexerFactory>()
                  .Setup(s => s.Find(4))
                  .Returns(new IndexerDefinition { Id = 4, Settings = new NewznabSettings { FailDownloads = failDownloads.Select(f => (int)f).ToArray() } });
        }

        private IFileInfo FileInfo(string path)
        {
            var file = new Mock<IFileInfo>();
            file.SetupGet(f => f.Name).Returns(System.IO.Path.GetFileName(path));
            file.SetupGet(f => f.FullName).Returns(path);
            return file.Object;
        }

        private IDirectoryInfo DirectoryInfo(string path)
        {
            var dir = new Mock<IDirectoryInfo>();
            dir.SetupGet(f => f.Name).Returns(System.IO.Path.GetFileName(path));
            dir.SetupGet(f => f.FullName).Returns(path);
            return dir.Object;
        }

        [Test]
        public void should_fail_a_single_file_executable_download_when_the_indexer_fails_executables()
        {
            var path = "/downloads/Artist - Album (2024) FLAC.exe";
            GivenDownload(path);
            GivenIndexerFails(FailDownloads.Executables);

            Mocker.GetMock<IDiskProvider>().Setup(s => s.FolderExists(path)).Returns(false);
            Mocker.GetMock<IDiskProvider>().Setup(s => s.FileExists(path)).Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(s => s.GetFileInfo(path)).Returns(FileInfo(path));

            Subject.Import(_trackedDownload);

            _trackedDownload.State.Should().Be(TrackedDownloadState.DownloadFailedPending);
        }

        private void GivenRejectedTrackBeside(string path, string unsafeFile)
        {
            var track = path + "/01 Track.flac";
            var rejectedTrack = new ImportDecision<LocalTrack>(new LocalTrack { Path = track }, new Rejection("Album match is not close enough"));

            Mocker.GetMock<IDiskProvider>().Setup(s => s.FolderExists(path)).Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(s => s.GetDirectoryInfo(path)).Returns(DirectoryInfo(path));
            Mocker.GetMock<IDiskProvider>().Setup(s => s.GetFiles(path, true)).Returns(new[] { track, path + unsafeFile });

            Mocker.GetMock<IMakeImportDecision>()
                  .Setup(v => v.GetImportDecisions(It.IsAny<List<IFileInfo>>(), It.IsAny<IdentificationOverrides>(), It.IsAny<ImportDecisionMakerInfo>(), It.IsAny<ImportDecisionMakerConfig>()))
                  .Returns(new List<ImportDecision<LocalTrack>> { rejectedTrack });

            Mocker.GetMock<IImportApprovedTracks>()
                  .Setup(s => s.Import(It.IsAny<List<ImportDecision<LocalTrack>>>(), It.IsAny<bool>(), It.IsAny<DownloadClientItem>(), It.IsAny<ImportMode>()))
                  .Returns(new List<ImportResult> { new ImportResult(rejectedTrack, "Album match is not close enough") });
        }

        [Test]
        public void should_fail_a_download_with_a_dangerous_file_beside_rejected_tracks_when_the_indexer_fails_them()
        {
            var path = "/downloads/Artist - Album (2024)";
            GivenDownload(path);
            GivenIndexerFails(FailDownloads.PotentiallyDangerous);
            GivenRejectedTrackBeside(path, "/Extras/Website.lnk");

            Subject.Import(_trackedDownload);

            _trackedDownload.State.Should().Be(TrackedDownloadState.DownloadFailedPending);
        }

        [Test]
        public void should_keep_import_failed_and_add_a_caution_for_rejected_tracks_beside_a_file_the_indexer_doesnt_fail()
        {
            var path = "/downloads/Artist - Album (2024)";
            GivenDownload(path);
            GivenIndexerFails();
            GivenRejectedTrackBeside(path, "/Extras/Website.lnk");

            Subject.Import(_trackedDownload);

            _trackedDownload.State.Should().Be(TrackedDownloadState.ImportFailed);
            _trackedDownload.StatusMessages.Select(m => m.Title).Should().Contain("01 Track.flac").And.Contain(t => t.StartsWith("Caution"));
        }
    }
}
