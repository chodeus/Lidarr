using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Music;
using NzbDrone.Core.Music.Events;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MusicTests
{
    [TestFixture]
    public class UnmonitorDeletedAlbumsServiceFixture : DbTest<UnmonitorDeletedAlbumsService, Album>
    {
        private ArtistMetadata _metadata;
        private int _next;

        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<IAlbumRepository>(Mocker.Resolve<AlbumRepository>());
            Mocker.SetConstant<IMediaFileRepository>(Mocker.Resolve<MediaFileRepository>());
            Mocker.SetConstant<IMediaFileService>(Mocker.Resolve<MediaFileService>());
            Mocker.SetConstant<IAlbumService>(Mocker.Resolve<AlbumService>());

            // The real services publish these after changing the database, so hand them on as the event aggregator would.
            Mocker.GetMock<IEventAggregator>()
                .Setup(s => s.PublishEvent(It.IsAny<TrackFileDeletedEvent>()))
                .Callback<TrackFileDeletedEvent>(e => Subject.Handle(e));

            Mocker.GetMock<IEventAggregator>()
                .Setup(s => s.PublishEvent(It.IsAny<AlbumDeletedEvent>()))
                .Callback<AlbumDeletedEvent>(e => Subject.Handle(e));

            GivenUnmonitorDeletedAlbums(true);

            _metadata = Db.Insert(Builder<ArtistMetadata>.CreateNew()
                .With(m => m.Id = 0)
                .Build());

            Db.Insert(Builder<Artist>.CreateNew()
                .With(a => a.Id = 0)
                .With(a => a.ArtistMetadataId = _metadata.Id)
                .Build());
        }

        private void GivenUnmonitorDeletedAlbums(bool enabled)
        {
            Mocker.GetMock<IConfigService>()
                .SetupGet(s => s.AutoUnmonitorPreviouslyDownloadedTracks)
                .Returns(enabled);
        }

        private void GivenFilesStillOnDisk()
        {
            Mocker.GetMock<IDiskProvider>()
                .Setup(s => s.FileExists(It.IsAny<string>()))
                .Returns(true);
        }

        private (Album Album, AlbumRelease Release, List<TrackFile> Files) GivenMonitoredAlbumWithFiles(int fileCount = 2)
        {
            var album = Db.Insert(Builder<Album>.CreateNew()
                .With(a => a.Id = 0)
                .With(a => a.ForeignAlbumId = $"album{++_next}")
                .With(a => a.ArtistMetadataId = _metadata.Id)
                .With(a => a.Monitored = true)
                .Build());

            var release = Db.Insert(Builder<AlbumRelease>.CreateNew()
                .With(r => r.Id = 0)
                .With(r => r.ForeignReleaseId = $"release{++_next}")
                .With(r => r.AlbumId = album.Id)
                .With(r => r.Monitored = true)
                .Build());

            var files = Enumerable.Range(0, fileCount).Select(_ => GivenTrackWithFile(album, release)).ToList();

            return (album, release, files);
        }

        private TrackFile GivenTrackWithFile(Album album, AlbumRelease release)
        {
            var file = Db.Insert(Builder<TrackFile>.CreateNew()
                .With(f => f.Id = 0)
                .With(f => f.Path = $"/music/Artist/Album/{++_next}.flac")
                .With(f => f.AlbumId = album.Id)
                .With(f => f.Quality = new QualityModel(Quality.FLAC))
                .Build());

            Db.Insert(Builder<Track>.CreateNew()
                .With(t => t.Id = 0)
                .With(t => t.ForeignTrackId = $"track{++_next}")
                .With(t => t.AlbumReleaseId = release.Id)
                .With(t => t.TrackFileId = file.Id)
                .Build());

            return file;
        }

        private void DeleteFiles(List<TrackFile> files, DeleteMediaFileReason reason)
        {
            Mocker.Resolve<IMediaFileService>().DeleteMany(files, reason);
        }

        private void CompleteScan()
        {
            Subject.Handle(new DiskScanCompletedEvent());
        }

        private bool IsMonitored(Album album)
        {
            return Storage.Get(album.Id).Monitored;
        }

        [Test]
        public void should_unmonitor_album_when_its_last_file_is_deleted()
        {
            var (album, _, files) = GivenMonitoredAlbumWithFiles();

            DeleteFiles(files, DeleteMediaFileReason.Manual);

            IsMonitored(album).Should().BeFalse();
        }

        [Test]
        public void should_keep_album_monitored_while_it_has_other_files()
        {
            var (album, _, files) = GivenMonitoredAlbumWithFiles();

            DeleteFiles(files.Take(1).ToList(), DeleteMediaFileReason.Manual);

            IsMonitored(album).Should().BeTrue();
        }

        [Test]
        public void should_keep_album_monitored_when_its_files_are_still_on_disk()
        {
            // Removing a root folder drops its files from the database but leaves them on disk.
            GivenFilesStillOnDisk();
            var (album, _, files) = GivenMonitoredAlbumWithFiles();

            DeleteFiles(files, DeleteMediaFileReason.Manual);

            IsMonitored(album).Should().BeTrue();
        }

        [TestCase(DeleteMediaFileReason.Upgrade)]
        [TestCase(DeleteMediaFileReason.ManualOverride)]
        public void should_keep_album_monitored_when_its_files_are_replaced(DeleteMediaFileReason reason)
        {
            var (album, _, files) = GivenMonitoredAlbumWithFiles();

            DeleteFiles(files, reason);
            CompleteScan();

            IsMonitored(album).Should().BeTrue();
        }

        [TestCase(DeleteMediaFileReason.Manual)]
        [TestCase(DeleteMediaFileReason.MissingFromDisk)]
        public void should_keep_album_monitored_when_disabled(DeleteMediaFileReason reason)
        {
            GivenUnmonitorDeletedAlbums(false);
            var (album, _, files) = GivenMonitoredAlbumWithFiles();

            DeleteFiles(files, reason);
            CompleteScan();

            IsMonitored(album).Should().BeTrue();
        }

        [Test]
        public void should_keep_album_missing_from_disk_monitored_until_the_scan_completes()
        {
            var (album, _, files) = GivenMonitoredAlbumWithFiles();

            DeleteFiles(files, DeleteMediaFileReason.MissingFromDisk);

            IsMonitored(album).Should().BeTrue();
        }

        [Test]
        public void should_unmonitor_album_missing_from_disk_when_the_scan_completes()
        {
            var (album, _, files) = GivenMonitoredAlbumWithFiles();

            DeleteFiles(files, DeleteMediaFileReason.MissingFromDisk);
            CompleteScan();

            IsMonitored(album).Should().BeFalse();
        }

        [Test]
        public void should_keep_album_monitored_when_the_scan_imports_its_files_from_a_new_path()
        {
            var (album, release, files) = GivenMonitoredAlbumWithFiles(1);

            DeleteFiles(files, DeleteMediaFileReason.MissingFromDisk);
            GivenTrackWithFile(album, release);
            CompleteScan();

            IsMonitored(album).Should().BeTrue();
        }

        [Test]
        public void should_not_unmonitor_album_again_on_a_later_scan()
        {
            var (album, _, files) = GivenMonitoredAlbumWithFiles();

            DeleteFiles(files, DeleteMediaFileReason.MissingFromDisk);
            CompleteScan();
            Mocker.Resolve<IAlbumService>().SetAlbumMonitored(album.Id, true);
            CompleteScan();

            IsMonitored(album).Should().BeTrue();
        }

        [Test]
        public void should_unmonitor_the_other_albums_when_one_is_deleted_during_the_scan()
        {
            var (deleted, _, deletedFiles) = GivenMonitoredAlbumWithFiles();
            var (album, _, files) = GivenMonitoredAlbumWithFiles();

            DeleteFiles(deletedFiles, DeleteMediaFileReason.MissingFromDisk);
            DeleteFiles(files, DeleteMediaFileReason.MissingFromDisk);
            Mocker.Resolve<IAlbumService>().DeleteAlbum(deleted.Id, false);
            CompleteScan();

            IsMonitored(album).Should().BeFalse();
        }
    }
}
