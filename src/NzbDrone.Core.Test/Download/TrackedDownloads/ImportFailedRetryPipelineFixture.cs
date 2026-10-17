using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using FluentAssertions.Execution;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.MediaFiles.TrackImport;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Music;
using NzbDrone.Core.Music.Commands;
using NzbDrone.Core.Music.Events;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Download.TrackedDownloads
{
    // Real: ImportFailedRetryService, TrackedDownloadService, ParsingService, DownloadMonitoringService,
    // DownloadProcessingService, CompletedDownloadService, FailedDownloadService, TrackedDownloadAlreadyImported.
    // Mocked: DB services, the download client (HTTP) and the file import (disk).
    [TestFixture]
    public class ImportFailedRetryPipelineFixture : CoreTest
    {
        private const string DownloadId = "dl-1";

        private Artist _artist;
        private Album _album;
        private DownloadClientItem _downloadItem;
        private EntityHistory _grabbed;
        private TrackedDownloadService _trackedDownloadService;
        private DownloadMonitoringService _monitoringService;
        private DownloadProcessingService _processingService;
        private ImportFailedRetryService _retryService;

        [SetUp]
        public void Setup()
        {
            _artist = new Artist { Id = 5, ArtistMetadataId = 7 };
            _album = CreateAlbum(4, 10);

            var client = new DownloadClientDefinition { Id = 1, Name = "sab", Protocol = "UsenetDownloadProtocol", RemoveCompletedDownloads = true };

            _downloadItem = new DownloadClientItem
            {
                DownloadClientInfo = new DownloadClientItemClientInfo { Id = 1, Name = "sab", Protocol = client.Protocol, RemoveCompletedDownloads = true },
                DownloadId = DownloadId,
                Title = "Audio Artist - Audio Album [2018 - FLAC]",
                Category = "music",
                Status = DownloadItemStatus.Completed,
                OutputPath = new OsPath("/downloads/Audio Artist - Audio Album".AsOsAgnostic()),
                CanBeRemoved = true,
                CanMoveFiles = true
            };

            _grabbed = new EntityHistory
            {
                Id = 1,
                DownloadId = DownloadId,
                EventType = EntityHistoryEventType.Grabbed,
                ArtistId = _artist.Id,
                AlbumId = _album.Id,
                SourceTitle = _downloadItem.Title,
                Date = DateTime.UtcNow.AddHours(-2)
            };

            Mocker.GetMock<IHistoryService>().Setup(s => s.FindByDownloadId(DownloadId)).Returns(() => new List<EntityHistory> { _grabbed });
            Mocker.GetMock<IHistoryService>().Setup(s => s.MostRecentForDownloadId(DownloadId)).Returns(() => _grabbed);
            Mocker.GetMock<IHistoryService>().Setup(s => s.Find(DownloadId, EntityHistoryEventType.Grabbed)).Returns(() => new List<EntityHistory> { _grabbed });

            Mocker.GetMock<IArtistService>().Setup(s => s.FindByName(It.IsAny<string>())).Returns(_artist);
            GivenAlbumFoundByTitle(_album);
            Mocker.GetMock<IAlbumService>().Setup(s => s.GetAlbums(It.IsAny<IEnumerable<int>>())).Returns(() => new List<Album> { CreateAlbum(4, 10) });

            Mocker.GetMock<IProvideImportItemService>()
                  .Setup(s => s.ProvideImportItem(It.IsAny<DownloadClientItem>(), It.IsAny<DownloadClientItem>()))
                  .Returns<DownloadClientItem, DownloadClientItem>((i, p) => i);

            Mocker.GetMock<IConfigService>().SetupGet(s => s.EnableCompletedDownloadHandling).Returns(true);

            Mocker.GetMock<IDownloadClient>().SetupGet(c => c.Definition).Returns(client);
            Mocker.GetMock<IDownloadClient>().Setup(c => c.GetItems()).Returns(() => new List<DownloadClientItem> { _downloadItem.Clone() });
            Mocker.GetMock<IDownloadClientFactory>()
                  .Setup(s => s.DownloadHandlingEnabled(It.IsAny<bool>()))
                  .Returns(() => new List<IDownloadClient> { Mocker.GetMock<IDownloadClient>().Object });

            GivenImportRejected();

            Mocker.SetConstant<IParsingService>(Mocker.Resolve<ParsingService>());
            Mocker.SetConstant<ITrackedDownloadAlreadyImported>(Mocker.Resolve<TrackedDownloadAlreadyImported>());
            Mocker.SetConstant<IFailedDownloadService>(Mocker.Resolve<FailedDownloadService>());
            Mocker.SetConstant<ICompletedDownloadService>(Mocker.Resolve<CompletedDownloadService>());

            _trackedDownloadService = Mocker.Resolve<TrackedDownloadService>();
            Mocker.SetConstant<ITrackedDownloadService>(_trackedDownloadService);

            _monitoringService = Mocker.Resolve<DownloadMonitoringService>();
            _processingService = Mocker.Resolve<DownloadProcessingService>();
            _retryService = Mocker.Resolve<ImportFailedRetryService>();
        }

        private static Album CreateAlbum(int id, int trackCount)
        {
            return new Album
            {
                Id = id,
                ArtistMetadataId = 7,
                Title = "Audio Album",
                AlbumReleases = new List<AlbumRelease> { new AlbumRelease { Monitored = true, TrackCount = trackCount } }
            };
        }

        private void GivenAlbumFoundByTitle(Album album)
        {
            Mocker.GetMock<IAlbumService>().Setup(s => s.FindByTitleAndYear(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int?>())).Returns(album);
            Mocker.GetMock<IAlbumService>().Setup(s => s.FindByTitleAndYearInexact(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int?>())).Returns(album);
        }

        private void GivenImportRejected()
        {
            Mocker.GetMock<IDownloadedTracksImportService>()
                  .Setup(s => s.ProcessPath(It.IsAny<string>(), It.IsAny<ImportMode>(), It.IsAny<Artist>(), It.IsAny<DownloadClientItem>()))
                  .Returns(() => new List<ImportResult>
                  {
                      Rejected("/downloads/Audio Artist - Audio Album/01.flac"),
                      Rejected("/downloads/Audio Artist - Audio Album/02.flac")
                  });
        }

        private void GivenNothingToImport()
        {
            Mocker.GetMock<IDownloadedTracksImportService>()
                  .Setup(s => s.ProcessPath(It.IsAny<string>(), It.IsAny<ImportMode>(), It.IsAny<Artist>(), It.IsAny<DownloadClientItem>()))
                  .Returns(() => new List<ImportResult>());
        }

        private static ImportResult Rejected(string path)
        {
            var decision = new ImportDecision<LocalTrack>(new LocalTrack { Path = path.AsOsAgnostic() }, new Rejection("Album match is not close enough"));

            return new ImportResult(decision, "Album match is not close enough");
        }

        private int ProcessPathCalls()
        {
            return Mocker.GetMock<IDownloadedTracksImportService>().Invocations.Count(i => i.Method.Name == nameof(IDownloadedTracksImportService.ProcessPath));
        }

        private void RunMonitoringPass()
        {
            _monitoringService.Execute(new RefreshMonitoredDownloadsCommand());
            _processingService.Execute(new ProcessMonitoredDownloadsCommand());
        }

        private void RefreshFinished()
        {
            _retryService.Handle(new CommandExecutedEvent(new CommandModel { Body = new RefreshArtistCommand() }));
        }

        private TrackedDownload GivenImportFailedDownload()
        {
            RunMonitoringPass();

            var trackedDownload = _trackedDownloadService.Find(DownloadId);
            trackedDownload.State.Should().Be(TrackedDownloadState.ImportFailed);
            trackedDownload.RemoteAlbum.Albums.Should().ContainSingle();

            return trackedDownload;
        }

        [Test]
        public void should_not_mark_imported_or_remove_a_download_whose_album_no_longer_maps()
        {
            var trackedDownload = GivenImportFailedDownload();

            // The album is deleted (by the user, or by a refresh after MusicBrainz drops it); the title no longer maps to it.
            GivenAlbumFoundByTitle(null);
            _trackedDownloadService.Handle(new AlbumDeletedEvent(_album, false, false));
            trackedDownload.RemoteAlbum.Artist.Should().BeSameAs(_artist);
            trackedDownload.RemoteAlbum.Albums.Should().BeEmpty();

            // A later refresh updates another album of the same artist.
            _retryService.Handle(new AlbumUpdatedEvent(CreateAlbum(9, 12)));
            RefreshFinished();
            RunMonitoringPass();

            using (new AssertionScope())
            {
                trackedDownload.State.Should().NotBe(TrackedDownloadState.Imported);
                Mocker.GetMock<IEventAggregator>().Invocations
                      .Count(i => i.Arguments[0] is DownloadCanBeRemovedEvent)
                      .Should().Be(0, "nothing was imported, so the download client must not delete the download");
            }
        }

        [Test]
        public void control_without_the_retry_an_unmapped_download_stays_import_failed()
        {
            var trackedDownload = GivenImportFailedDownload();

            GivenAlbumFoundByTitle(null);
            _trackedDownloadService.Handle(new AlbumDeletedEvent(_album, false, false));

            RunMonitoringPass();

            trackedDownload.State.Should().Be(TrackedDownloadState.ImportFailed);
            VerifyEventNotPublished<DownloadCanBeRemovedEvent>();
        }

        [Test]
        public void a_retry_whose_tracks_are_rejected_again_should_return_to_import_failed()
        {
            var trackedDownload = GivenImportFailedDownload();

            _retryService.Handle(new AlbumUpdatedEvent(_album));
            RefreshFinished();
            RunMonitoringPass();
            RunMonitoringPass();

            using (new AssertionScope())
            {
                trackedDownload.State.Should().Be(TrackedDownloadState.ImportFailed);
                ProcessPathCalls().Should().Be(2, "one original import plus one retry");
            }
        }

        [Test]
        public void a_retry_that_finds_nothing_to_import_should_wait_as_import_pending()
        {
            var trackedDownload = GivenImportFailedDownload();

            // The download folder was emptied after the first failure; a first attempt waits the same way.
            GivenNothingToImport();

            _retryService.Handle(new AlbumUpdatedEvent(_album));
            RefreshFinished();
            RunMonitoringPass();

            trackedDownload.State.Should().Be(TrackedDownloadState.ImportPending);
        }

        [Test]
        public void one_refresh_should_retry_a_download_at_most_once()
        {
            GivenImportFailedDownload();

            // One artist refresh publishes AlbumUpdatedEvent per changed album; the 1-minute monitoring task runs between them.
            _retryService.Handle(new AlbumUpdatedEvent(CreateAlbum(9, 12)));
            RunMonitoringPass();
            _retryService.Handle(new AlbumUpdatedEvent(CreateAlbum(10, 12)));
            RunMonitoringPass();
            RefreshFinished();
            RunMonitoringPass();

            using (new AssertionScope())
            {
                ProcessPathCalls().Should().Be(2, "one original import plus one retry");
                Mocker.GetMock<IEventAggregator>().Invocations
                      .Count(i => i.Arguments[0] is AlbumImportIncompleteEvent)
                      .Should().Be(2, "each AlbumImportIncompleteEvent sends an Import Failure notification");
            }
        }

        [Test]
        public void manual_import_that_finished_before_the_retry_is_left_alone()
        {
            var trackedDownload = GivenImportFailedDownload();

            // ManualImportService.cs:467 on the ManualImport command thread.
            trackedDownload.State = TrackedDownloadState.Imported;

            _retryService.Handle(new AlbumUpdatedEvent(_album));
            RefreshFinished();

            trackedDownload.State.Should().Be(TrackedDownloadState.Imported);
        }

        [Test]
        public void manual_import_that_finishes_during_the_retry_should_not_be_overwritten()
        {
            var trackedDownload = GivenImportFailedDownload();

            // RefreshArtist (not RequiresDiskAccess) and ManualImport can run together; the manual import finishes during the DB read.
            Mocker.GetMock<IAlbumService>()
                  .Setup(s => s.GetAlbums(It.IsAny<IEnumerable<int>>()))
                  .Callback(() => trackedDownload.State = TrackedDownloadState.Imported)
                  .Returns(() => new List<Album> { CreateAlbum(4, 10) });

            _retryService.Handle(new AlbumUpdatedEvent(_album));
            RefreshFinished();

            using (new AssertionScope())
            {
                trackedDownload.State.Should().Be(TrackedDownloadState.Imported);

                // Manual import moved the files away, so the next passes find nothing and the download is never removed.
                GivenNothingToImport();
                RunMonitoringPass();
                trackedDownload.State.Should().Be(TrackedDownloadState.Imported);
            }
        }
    }
}
