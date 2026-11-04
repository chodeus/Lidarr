using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.TrackImport;
using NzbDrone.Core.Music;
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

            _artist = Builder<Artist>.CreateNew().With(a => a.ArtistMetadataId = 11).BuildNew();
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
                .With(a => a.ArtistMetadataId = artistMetadataId)
                .With(a => a.ForeignAlbumId = System.Guid.NewGuid().ToString())
                .BuildNew();
            Db.Insert(album);
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
            typeof(ICompletedDownloadService).GetMethod(nameof(ICompletedDownloadService.GrabbedAlbum))
                .IsAbstract.Should().BeFalse();
        }
    }
}
