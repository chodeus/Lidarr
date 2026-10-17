using System;
using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Music;
using NzbDrone.Core.Music.Commands;
using NzbDrone.Core.Music.Events;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Download.TrackedDownloads
{
    // Real AlbumService and AlbumRepository over the test database, in place of ImportFailedRetryServiceFixture's GetAlbums stub
    [TestFixture]
    public class ImportFailedRetryDbFixture : DbTest<ImportFailedRetryService, Album>
    {
        private Album _album;
        private List<TrackedDownload> _trackedDownloads;

        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<IAlbumRepository>(Mocker.Resolve<AlbumRepository>());
            Mocker.SetConstant<IAlbumService>(Mocker.Resolve<AlbumService>());

            var albumRepo = Mocker.Resolve<AlbumRepository>();
            var releaseRepo = Mocker.Resolve<ReleaseRepository>();

            _album = new Album
            {
                Title = "Audio Album",
                ForeignAlbumId = "fa-1",
                CleanTitle = "audioalbum",
                ArtistMetadataId = 7,
                AlbumType = "Album",
                Added = DateTime.UtcNow
            };
            albumRepo.Insert(_album);
            releaseRepo.Insert(Builder<AlbumRelease>.CreateNew()
                .With(r => r.Id = 0)
                .With(r => r.AlbumId = _album.Id)
                .With(r => r.ForeignReleaseId = "fr-1")
                .With(r => r.Monitored = true)
                .With(r => r.TrackCount = 10)
                .Build());

            _trackedDownloads = new List<TrackedDownload>();
            Mocker.GetMock<ITrackedDownloadService>().Setup(s => s.GetTrackedDownloads()).Returns(_trackedDownloads);
        }

        private TrackedDownload GivenFailedDownload(params Album[] albums)
        {
            var trackedDownload = new TrackedDownload
            {
                State = TrackedDownloadState.ImportFailed,
                DownloadItem = new DownloadClientItem { Title = "Audio Artist - Audio Album" },
                RemoteAlbum = new RemoteAlbum { Artist = new Artist { Id = 5, ArtistMetadataId = 7 }, Albums = albums.ToList() }
            };

            _trackedDownloads.Add(trackedDownload);

            return trackedDownload;
        }

        private void Refresh()
        {
            Subject.Handle(new AlbumUpdatedEvent(_album));
            Subject.Handle(new CommandExecutedEvent(new CommandModel { Body = new RefreshArtistCommand() }));
        }

        private static Album StaleCopy(int id)
        {
            return new Album { Id = id, ArtistMetadataId = 7, AlbumReleases = new List<AlbumRelease> { new AlbumRelease { Monitored = true, TrackCount = 12 } } };
        }

        [Test]
        public void swap_should_load_the_refreshed_release_track_count_from_the_database()
        {
            var trackedDownload = GivenFailedDownload(StaleCopy(_album.Id));

            Refresh();

            trackedDownload.State.Should().Be(TrackedDownloadState.ImportBlocked);
            trackedDownload.RemoteAlbum.Albums.Sum(a => a.AlbumReleases.Value.Where(r => r.Monitored).Sum(r => r.TrackCount)).Should().Be(10);
        }

        [Test]
        public void one_download_with_a_missing_album_should_not_block_retries_of_the_others()
        {
            var broken = GivenFailedDownload(StaleCopy(_album.Id), StaleCopy(_album.Id + 1000));
            var healthy = GivenFailedDownload(StaleCopy(_album.Id));

            Refresh();

            broken.State.Should().Be(TrackedDownloadState.ImportFailed);
            healthy.State.Should().Be(TrackedDownloadState.ImportBlocked);
            ExceptionVerification.ExpectedWarns(1);
        }
    }
}
