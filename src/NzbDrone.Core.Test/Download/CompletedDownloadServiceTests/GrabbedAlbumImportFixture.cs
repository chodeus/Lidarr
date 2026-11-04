using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.TrackImport;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Download.CompletedDownloadServiceTests
{
    // Real history repository: the grab rows' joined Album is what the import is scoped to.
    [TestFixture]
    public class GrabbedAlbumImportFixture : DbTest<CompletedDownloadService, EntityHistory>
    {
        private const string DownloadId = "grab-1";

        private Artist _artist;
        private TrackedDownload _trackedDownload;

        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<IHistoryRepository>(Mocker.Resolve<EntityHistoryRepository>());
            Mocker.SetConstant<IHistoryService>(Mocker.Resolve<EntityHistoryService>());
            Mocker.SetConstant<IArtistRepository>(Mocker.Resolve<ArtistRepository>());
            Mocker.SetConstant<IAlbumRepository>(Mocker.Resolve<AlbumRepository>());
            Mocker.SetConstant<IArtistService>(Mocker.Resolve<ArtistService>());
            Mocker.SetConstant<IAlbumService>(Mocker.Resolve<AlbumService>());
            Mocker.SetConstant<IParsingService>(Mocker.Resolve<ParsingService>());

            var metadata = Builder<ArtistMetadata>.CreateNew().With(m => m.Id = 0).BuildNew();
            Db.Insert(metadata);

            _artist = Builder<Artist>.CreateNew().With(a => a.Id = 0).With(a => a.ArtistMetadataId = metadata.Id).BuildNew();
            Db.Insert(_artist);

            var completed = Builder<DownloadClientItem>.CreateNew()
                .With(d => d.DownloadId = DownloadId)
                .With(d => d.Status = DownloadItemStatus.Completed)
                .With(d => d.OutputPath = new OsPath(@"C:\DropFolder\Artist - Single".AsOsAgnostic()))
                .Build();

            _trackedDownload = Builder<TrackedDownload>.CreateNew()
                .With(t => t.State = TrackedDownloadState.ImportPending)
                .With(t => t.DownloadItem = completed)
                .With(t => t.RemoteAlbum = new RemoteAlbum
                {
                    Artist = _artist,
                    Albums = new List<Album> { new Album { Id = 999, AlbumReleases = new List<AlbumRelease> { new AlbumRelease { Monitored = true, TrackCount = 1 } } } }
                })
                .Build();

            Mocker.GetMock<IProvideImportItemService>()
                .Setup(s => s.ProvideImportItem(It.IsAny<DownloadClientItem>(), It.IsAny<DownloadClientItem>()))
                .Returns<DownloadClientItem, DownloadClientItem>((i, p) => i);

            Mocker.GetMock<IDownloadedTracksImportService>()
                .Setup(s => s.ProcessPath(It.IsAny<string>(), It.IsAny<ImportMode>(), It.IsAny<Artist>(), It.IsAny<DownloadClientItem>()))
                .Returns(new List<ImportResult>());

            Mocker.GetMock<IDownloadedTracksImportService>()
                .Setup(s => s.ProcessGrabbedPath(It.IsAny<string>(), It.IsAny<ImportMode>(), It.IsAny<Artist>(), It.IsAny<Album>(), It.IsAny<DownloadClientItem>()))
                .Returns(new List<ImportResult>());
        }

        private Album GivenAlbum(int artistMetadataId)
        {
            var album = Builder<Album>.CreateNew()
                .With(a => a.Id = 0)
                .With(a => a.ArtistMetadataId = artistMetadataId)
                .With(a => a.ForeignAlbumId = System.Guid.NewGuid().ToString())
                .BuildNew();
            Db.Insert(album);
            Db.Insert(Builder<AlbumRelease>.CreateNew()
                .With(r => r.Id = 0)
                .With(r => r.AlbumId = album.Id)
                .With(r => r.ForeignReleaseId = System.Guid.NewGuid().ToString())
                .With(r => r.Monitored = true)
                .With(r => r.TrackCount = 3)
                .BuildNew());
            return album;
        }

        private void GivenGrab(Album album)
        {
            Db.Insert(Builder<EntityHistory>.CreateNew()
                .With(h => h.EventType = EntityHistoryEventType.Grabbed)
                .With(h => h.DownloadId = DownloadId)
                .With(h => h.ArtistId = _artist.Id)
                .With(h => h.AlbumId = album.Id)
                .With(h => h.Quality = new QualityModel())
                .BuildNew());
        }

        // The title-parsed album is the artist's other album of that name, not a stand-in the history join drops.
        private void GivenParsedAlbumInLibrary()
        {
            _trackedDownload.RemoteAlbum.Albums = Mocker.Resolve<AlbumService>().GetAlbums(new[] { GivenAlbum(_artist.ArtistMetadataId).Id });
        }

        private void GivenRealCompletionHistory()
        {
            var history = Mocker.Resolve<EntityHistoryService>();
            Mocker.SetConstant<ITrackedDownloadAlreadyImported>(Mocker.Resolve<TrackedDownloadAlreadyImported>());
            Mocker.GetMock<IEventAggregator>()
                .Setup(e => e.PublishEvent(It.IsAny<DownloadCompletedEvent>()))
                .Callback<DownloadCompletedEvent>(history.Handle);
        }

        private void GivenGrabbedPathResults(Album album, string rejection)
        {
            var results = Enumerable.Range(1, 3).Select(i =>
            {
                var track = new LocalTrack { Path = $@"C:\DropFolder\Artist - Single\{i:00}.flac".AsOsAgnostic(), Album = album, Tracks = new List<Track> { new Track() } };
                return rejection == null
                    ? new ImportResult(new ImportDecision<LocalTrack>(track))
                    : new ImportResult(new ImportDecision<LocalTrack>(track, new Rejection(rejection)), rejection);
            }).ToList();

            Mocker.GetMock<IDownloadedTracksImportService>()
                .Setup(s => s.ProcessGrabbedPath(It.IsAny<string>(), It.IsAny<ImportMode>(), It.IsAny<Artist>(), It.IsAny<Album>(), It.IsAny<DownloadClientItem>()))
                .Returns(results);
        }

        private List<int> ImportedAlbumIds() =>
            Mocker.Resolve<EntityHistoryService>().FindByDownloadId(DownloadId)
                .Where(h => h.EventType == EntityHistoryEventType.DownloadImported)
                .Select(h => h.AlbumId)
                .ToList();

        private void VerifyDevelopsImport()
        {
            Mocker.GetMock<IDownloadedTracksImportService>()
                .Verify(s => s.ProcessPath(It.IsAny<string>(), ImportMode.Auto, _artist, _trackedDownload.DownloadItem), Times.Once());
            Mocker.GetMock<IDownloadedTracksImportService>()
                .Verify(s => s.ProcessGrabbedPath(It.IsAny<string>(), It.IsAny<ImportMode>(), It.IsAny<Artist>(), It.IsAny<Album>(), It.IsAny<DownloadClientItem>()), Times.Never());
        }

        // The queue's album was parsed from the title (999); the grab names album.Id.
        [Test]
        public void should_import_against_the_album_the_grab_named()
        {
            var album = GivenAlbum(_artist.ArtistMetadataId);
            GivenGrab(album);

            Subject.Import(_trackedDownload);

            Mocker.GetMock<IDownloadedTracksImportService>()
                .Verify(s => s.ProcessGrabbedPath(It.IsAny<string>(), ImportMode.Auto, _artist, It.Is<Album>(a => a.Id == album.Id), _trackedDownload.DownloadItem), Times.Once());
            Mocker.GetMock<IDownloadedTracksImportService>()
                .Verify(s => s.ProcessPath(It.IsAny<string>(), It.IsAny<ImportMode>(), It.IsAny<Artist>(), It.IsAny<DownloadClientItem>()), Times.Never());
        }

        // Verification, the already-imported check and history read the tracked download's album, not the import's.
        [Test]
        public void should_point_the_tracked_download_at_the_grabbed_album()
        {
            var album = GivenAlbum(_artist.ArtistMetadataId);
            GivenGrab(album);

            Subject.Import(_trackedDownload);

            _trackedDownload.RemoteAlbum.Albums.Should().ContainSingle().Which.Id.Should().Be(album.Id);
            _trackedDownload.RemoteAlbum.Albums[0].AlbumReleases.Value.Should().ContainSingle().Which.TrackCount.Should().Be(3);
        }

        // A grab of several albums stays one after an album is deleted and its rows drop out of the history join.
        [Test]
        public void should_keep_develops_import_when_another_grabbed_album_was_deleted()
        {
            var kept = GivenAlbum(_artist.ArtistMetadataId);
            var gone = GivenAlbum(_artist.ArtistMetadataId);
            GivenGrab(kept);
            GivenGrab(gone);
            _trackedDownload.RemoteAlbum.Albums = Mocker.Resolve<AlbumService>().GetAlbums(new[] { kept.Id, gone.Id });
            Db.Delete(gone);

            Subject.Import(_trackedDownload);

            VerifyDevelopsImport();
            _trackedDownload.RemoteAlbum.Albums.Should().HaveCount(2);
        }

        [Test]
        public void should_record_the_import_against_the_grabbed_album()
        {
            var album = GivenAlbum(_artist.ArtistMetadataId);
            GivenGrab(album);
            GivenParsedAlbumInLibrary();
            GivenRealCompletionHistory();
            GivenGrabbedPathResults(album, null);

            Subject.Import(_trackedDownload);

            _trackedDownload.State.Should().Be(TrackedDownloadState.Imported);
            ImportedAlbumIds().Should().Equal(album.Id);
        }

        // A second pass finds every file already imported into the grabbed album.
        [Test]
        public void should_verify_a_repeat_pass_against_the_grabbed_albums_history()
        {
            var album = GivenAlbum(_artist.ArtistMetadataId);
            GivenGrab(album);
            GivenParsedAlbumInLibrary();
            GivenRealCompletionHistory();
            Db.Insert(Builder<EntityHistory>.CreateNew()
                .With(h => h.Id = 0)
                .With(h => h.EventType = EntityHistoryEventType.TrackFileImported)
                .With(h => h.DownloadId = DownloadId)
                .With(h => h.ArtistId = _artist.Id)
                .With(h => h.AlbumId = album.Id)
                .With(h => h.Date = System.DateTime.UtcNow)
                .With(h => h.Quality = new QualityModel())
                .BuildNew());
            GivenGrabbedPathResults(album, "Not an upgrade");

            Subject.Import(_trackedDownload);

            _trackedDownload.State.Should().Be(TrackedDownloadState.Imported);
        }

        [Test]
        public void should_import_against_the_album_when_it_was_grabbed_twice()
        {
            var album = GivenAlbum(_artist.ArtistMetadataId);
            GivenGrab(album);
            GivenGrab(album);

            Subject.Import(_trackedDownload);

            Mocker.GetMock<IDownloadedTracksImportService>()
                .Verify(s => s.ProcessGrabbedPath(It.IsAny<string>(), ImportMode.Auto, _artist, It.Is<Album>(a => a.Id == album.Id), _trackedDownload.DownloadItem), Times.Once());
        }

        [Test]
        public void should_keep_develops_import_when_the_grab_named_several_albums()
        {
            GivenGrab(GivenAlbum(_artist.ArtistMetadataId));
            GivenGrab(GivenAlbum(_artist.ArtistMetadataId));

            Subject.Import(_trackedDownload);

            VerifyDevelopsImport();
        }

        [Test]
        public void should_keep_develops_import_when_the_grabbed_album_is_another_artists()
        {
            GivenGrab(GivenAlbum(_artist.ArtistMetadataId + 1));

            Subject.Import(_trackedDownload);

            VerifyDevelopsImport();
        }

        [Test]
        public void should_keep_develops_import_without_grab_history()
        {
            Subject.Import(_trackedDownload);

            VerifyDevelopsImport();
        }

        // Plugins built against develop implement or mock this interface without the new member.
        [Test]
        public void should_give_the_new_interface_member_a_default_body()
        {
            typeof(ICompletedDownloadService).GetMethod(nameof(ICompletedDownloadService.ScopeToGrabbedAlbum))
                .IsAbstract.Should().BeFalse();
        }
    }
}
