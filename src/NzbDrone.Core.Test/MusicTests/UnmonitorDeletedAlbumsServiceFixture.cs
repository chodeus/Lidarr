using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.DecisionEngine.Specifications.RssSync;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Music;
using NzbDrone.Core.Music.Events;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MusicTests
{
    [TestFixture]
    public class UnmonitorDeletedAlbumsServiceFixture : DbTest<UnmonitorDeletedAlbumsService, Album>
    {
        private string _rootFolderPath;
        private string _otherRootFolderPath;
        private RootFolder _rootFolder;
        private ArtistMetadata _metadata;
        private int _next;

        [SetUp]
        public void Setup()
        {
            _rootFolderPath = @"C:\Test\Music".AsOsAgnostic();
            _otherRootFolderPath = @"C:\Test\Other".AsOsAgnostic();

            Mocker.SetConstant<IAlbumRepository>(Mocker.Resolve<AlbumRepository>());
            Mocker.SetConstant<IMediaFileRepository>(Mocker.Resolve<MediaFileRepository>());
            Mocker.SetConstant<IRootFolderRepository>(Mocker.Resolve<RootFolderRepository>());
            Mocker.SetConstant<IMediaFileService>(Mocker.Resolve<MediaFileService>());
            Mocker.SetConstant<IAlbumService>(Mocker.Resolve<AlbumService>());
            Mocker.SetConstant<IRootFolderService>(Mocker.Resolve<RootFolderService>());

            // The real services publish this after changing the database, so hand it on as the event aggregator would.
            Mocker.GetMock<IEventAggregator>()
                .Setup(s => s.PublishEvent(It.IsAny<TrackFileDeletedEvent>()))
                .Callback<TrackFileDeletedEvent>(e => Subject.Handle(e));

            Mocker.GetMock<IEventAggregator>()
                .Setup(s => s.PublishEvent(It.IsAny<ModelEvent<RootFolder>>()))
                .Callback<ModelEvent<RootFolder>>(e => Subject.Handle(e));

            GivenUnmonitorDeletedAlbums(true);

            _rootFolder = Db.Insert(Builder<RootFolder>.CreateNew()
                .With(r => r.Id = 0)
                .With(r => r.Path = _rootFolderPath)
                .Build());

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

        private void GivenRootFolderOnDisk(bool empty)
        {
            Mocker.GetMock<IDiskProvider>()
                .Setup(s => s.FolderExists(_rootFolderPath))
                .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                .Setup(s => s.FolderEmpty(_rootFolderPath))
                .Returns(empty);
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

            var release = GivenRelease(album, true);

            var files = Enumerable.Range(0, fileCount).Select(_ => GivenTrackWithFile(album, release)).ToList();

            return (album, release, files);
        }

        private AlbumRelease GivenRelease(Album album, bool monitored)
        {
            return Db.Insert(Builder<AlbumRelease>.CreateNew()
                .With(r => r.Id = 0)
                .With(r => r.ForeignReleaseId = $"release{++_next}")
                .With(r => r.AlbumId = album.Id)
                .With(r => r.Monitored = monitored)
                .Build());
        }

        private TrackFile GivenFile(Album album)
        {
            return Db.Insert(Builder<TrackFile>.CreateNew()
                .With(f => f.Id = 0)
                .With(f => f.Path = Path.Combine(_rootFolderPath, "Artist", "Album", $"{++_next}.flac"))
                .With(f => f.AlbumId = album.Id)
                .With(f => f.Quality = new QualityModel(Quality.FLAC))
                .Build());
        }

        private TrackFile GivenTrackWithFile(Album album, AlbumRelease release)
        {
            var file = GivenFile(album);

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
            CompleteScanOf(_rootFolderPath);
        }

        private void CompleteScanOf(params string[] rootFolders)
        {
            Subject.Handle(new DiskScanCompletedEvent(rootFolders.ToList(), new HashSet<int>()));
        }

        private void CompleteScanMatching(Album album)
        {
            Subject.Handle(new DiskScanCompletedEvent(new List<string> { _rootFolderPath }, new HashSet<int> { album.Id }));
        }

        private void CompleteArtistFolderScanMatching(Album album)
        {
            Subject.Handle(new DiskScanCompletedEvent(new List<string>(), new HashSet<int> { album.Id }));
        }

        private bool HoldsBackRssReleases(Album album)
        {
            Mocker.SetConstant<IUnmonitorDeletedAlbumsService>(Subject);
            var release = new RemoteAlbum { Artist = new Artist(), Albums = new List<Album> { album } };

            return !Mocker.Resolve<DeletedTrackFileSpecification>().IsSatisfiedBy(release, null).Accepted;
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
        public void should_keep_album_monitored_while_it_has_files_on_another_release()
        {
            var (album, _, files) = GivenMonitoredAlbumWithFiles(1);
            GivenTrackWithFile(album, GivenRelease(album, false));

            DeleteFiles(files, DeleteMediaFileReason.Manual);

            IsMonitored(album).Should().BeTrue();
        }

        [Test]
        public void should_keep_album_monitored_while_it_has_files_not_matched_to_tracks()
        {
            // Changing an album's release unlinks its files from their tracks until a rescan matches them again.
            var (album, _, files) = GivenMonitoredAlbumWithFiles(1);
            GivenFile(album);

            DeleteFiles(files, DeleteMediaFileReason.Manual);

            IsMonitored(album).Should().BeTrue();
        }

        [Test]
        public void should_keep_album_monitored_when_its_files_are_still_on_disk()
        {
            GivenFilesStillOnDisk();
            var (album, _, files) = GivenMonitoredAlbumWithFiles();

            DeleteFiles(files, DeleteMediaFileReason.Manual);

            IsMonitored(album).Should().BeTrue();
        }

        [Test]
        public void should_keep_album_monitored_when_a_scan_drops_files_still_on_disk()
        {
            // Scans skip some folders, such as Extras, and drop the files in them from the database as if missing.
            GivenFilesStillOnDisk();
            var (album, _, files) = GivenMonitoredAlbumWithFiles();

            DeleteFiles(files, DeleteMediaFileReason.MissingFromDisk);
            CompleteScan();

            IsMonitored(album).Should().BeTrue();
        }

        [Test]
        public void should_keep_album_monitored_when_its_root_folder_is_removed()
        {
            // Removing a root folder drops its files from the database in the background and leaves them where they are,
            // even while its storage is offline and after the same path has been added back.
            var (album, _, _) = GivenMonitoredAlbumWithFiles();
            GivenRootFolderOnDisk(false);

            Mocker.Resolve<MediaFileService>().HandleAsync(new ModelEvent<RootFolder>(_rootFolder, ModelAction.Deleted));

            IsMonitored(album).Should().BeTrue();
        }

        [Test]
        public void should_unmonitor_album_when_deleting_its_last_file_empties_the_root_folder()
        {
            // With Delete Empty Folders on, deleting an artist's last file removes the artist folder, which can be the
            // root folder's only one.
            var (album, _, files) = GivenMonitoredAlbumWithFiles();
            GivenRootFolderOnDisk(true);

            DeleteFiles(files, DeleteMediaFileReason.Manual);

            IsMonitored(album).Should().BeFalse();
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
        public void should_keep_album_monitored_when_disabled_before_the_scan_completes()
        {
            var (album, _, files) = GivenMonitoredAlbumWithFiles();

            DeleteFiles(files, DeleteMediaFileReason.MissingFromDisk);
            GivenUnmonitorDeletedAlbums(false);
            CompleteScan();
            GivenUnmonitorDeletedAlbums(true);
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
        public void should_unmonitor_album_missing_from_two_root_folders_once_each_has_been_scanned_in_full()
        {
            // The folder watcher scans only the root folders it saw changes in, and adding a root folder only that one.
            Db.Insert(Builder<RootFolder>.CreateNew()
                .With(r => r.Id = 0)
                .With(r => r.Path = _otherRootFolderPath)
                .Build());
            var (album, _, files) = GivenMonitoredAlbumWithFiles();
            files[1].Path = Path.Combine(_otherRootFolderPath, "Artist", "Album", Path.GetFileName(files[1].Path));
            Db.Update(files[1]);

            DeleteFiles(files, DeleteMediaFileReason.MissingFromDisk);

            CompleteScanOf(_rootFolderPath);
            IsMonitored(album).Should().BeTrue();

            CompleteScanOf(_otherRootFolderPath);
            IsMonitored(album).Should().BeFalse();
        }

        [Test]
        public void should_unmonitor_album_missing_from_disk_once_a_scan_of_its_whole_root_folder_completes()
        {
            var (album, _, files) = GivenMonitoredAlbumWithFiles();

            DeleteFiles(files, DeleteMediaFileReason.MissingFromDisk);

            // A scan of an artist folder, or of another root folder, could not have imported the files from where they went.
            CompleteScanOf();
            CompleteScanOf(_otherRootFolderPath);
            IsMonitored(album).Should().BeTrue();

            CompleteScanOf(_rootFolderPath);
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
        public void should_keep_album_monitored_when_the_scan_matched_files_to_it_that_it_did_not_import()
        {
            // Files moved to where they no longer match well enough to import are left unmapped, but are still the album's.
            var (album, _, files) = GivenMonitoredAlbumWithFiles();

            DeleteFiles(files, DeleteMediaFileReason.MissingFromDisk);
            CompleteScanMatching(album);
            CompleteScan();

            IsMonitored(album).Should().BeTrue();
        }

        [Test]
        public void should_keep_album_monitored_when_a_scan_of_an_artist_folder_matched_files_to_it_that_it_did_not_import()
        {
            // Later scans skip the files it left unmapped while they are unchanged, so only that scan saw them.
            var (album, _, files) = GivenMonitoredAlbumWithFiles();

            DeleteFiles(files, DeleteMediaFileReason.MissingFromDisk);
            CompleteArtistFolderScanMatching(album);
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
        public void should_not_decide_album_again_once_deleting_its_last_file_has_unmonitored_it()
        {
            var (album, _, files) = GivenMonitoredAlbumWithFiles();

            // A scan of the artist folder finds one file missing, then the other is deleted in Lidarr.
            DeleteFiles(files.Take(1).ToList(), DeleteMediaFileReason.MissingFromDisk);
            DeleteFiles(files.Skip(1).ToList(), DeleteMediaFileReason.Manual);
            Mocker.Resolve<IAlbumService>().SetAlbumMonitored(album.Id, true);

            HoldsBackRssReleases(album).Should().BeFalse();
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

        [Test]
        public void should_decide_the_other_albums_when_one_cannot_be_unmonitored()
        {
            var (first, _, firstFiles) = GivenMonitoredAlbumWithFiles();
            var (second, _, secondFiles) = GivenMonitoredAlbumWithFiles();
            var albums = new[] { first, second };
            var failed = false;

            // As when an album is deleted while it is unmonitored, which makes AlbumService.SetMonitored throw.
            Mocker.GetMock<IEventAggregator>()
                .Setup(s => s.PublishEvent(It.IsAny<AlbumEditedEvent>()))
                .Callback<AlbumEditedEvent>(e =>
                {
                    if (!failed)
                    {
                        failed = true;
                        throw new ApplicationException("Expected query to return 1 rows but returned 0");
                    }
                });

            DeleteFiles(firstFiles, DeleteMediaFileReason.MissingFromDisk);
            DeleteFiles(secondFiles, DeleteMediaFileReason.MissingFromDisk);
            CompleteScan();

            // The one that failed is decided again by the next scan of its root folder.
            albums.Count(HoldsBackRssReleases).Should().Be(1);
            albums.Should().OnlyContain(a => !IsMonitored(a));
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_not_unmonitor_albums_left_to_decide_when_their_root_folder_is_removed_during_the_scan()
        {
            // Removing the root folder can drop an album's remaining files before the scan has decided it.
            var (first, _, firstFiles) = GivenMonitoredAlbumWithFiles();
            var (second, _, secondFiles) = GivenMonitoredAlbumWithFiles();
            var removed = false;

            Mocker.GetMock<IEventAggregator>()
                .Setup(s => s.PublishEvent(It.IsAny<AlbumEditedEvent>()))
                .Callback<AlbumEditedEvent>(e =>
                {
                    if (!removed)
                    {
                        removed = true;
                        Mocker.Resolve<IRootFolderService>().Remove(_rootFolder.Id);
                    }
                });

            DeleteFiles(firstFiles, DeleteMediaFileReason.MissingFromDisk);
            DeleteFiles(secondFiles, DeleteMediaFileReason.MissingFromDisk);
            CompleteScan();

            new[] { first, second }.Count(IsMonitored).Should().Be(1);
        }

        [Test]
        public void should_hold_back_rss_releases_for_album_missing_from_disk_until_the_scan_completes()
        {
            var (album, _, files) = GivenMonitoredAlbumWithFiles();

            DeleteFiles(files, DeleteMediaFileReason.MissingFromDisk);

            HoldsBackRssReleases(album).Should().BeTrue();
        }

        [Test]
        public void should_hold_back_rss_releases_for_each_album_until_the_scan_has_unmonitored_it()
        {
            var (first, _, firstFiles) = GivenMonitoredAlbumWithFiles();
            var (second, _, secondFiles) = GivenMonitoredAlbumWithFiles();
            var albums = new[] { first, second };
            var held = new List<bool>();

            // Whenever the scan unmonitors one album, look at the other while it is still monitored without files.
            Mocker.GetMock<IEventAggregator>()
                .Setup(s => s.PublishEvent(It.IsAny<AlbumEditedEvent>()))
                .Callback<AlbumEditedEvent>(e => held.AddRange(albums.Where(a => a.Id != e.Album.Id && IsMonitored(a)).Select(HoldsBackRssReleases)));

            DeleteFiles(firstFiles, DeleteMediaFileReason.MissingFromDisk);
            DeleteFiles(secondFiles, DeleteMediaFileReason.MissingFromDisk);
            CompleteScan();

            held.Should().NotBeEmpty().And.OnlyContain(x => x);
            albums.Should().OnlyContain(a => !IsMonitored(a) && !HoldsBackRssReleases(a));
        }

        [Test]
        public void should_stop_holding_back_rss_releases_when_the_root_folder_is_removed()
        {
            // No scan covers a root folder once it is removed, so what scans found missing from it is never decided.
            var (album, _, files) = GivenMonitoredAlbumWithFiles();

            DeleteFiles(files, DeleteMediaFileReason.MissingFromDisk);
            Mocker.Resolve<IRootFolderService>().Remove(_rootFolder.Id);

            HoldsBackRssReleases(album).Should().BeFalse();
            IsMonitored(album).Should().BeTrue();
        }

        [Test]
        public void should_not_hold_back_rss_releases_for_files_a_scan_reports_after_their_root_folder_is_removed()
        {
            // A scan can still be reporting the files it found missing when their root folder is removed.
            var (album, _, files) = GivenMonitoredAlbumWithFiles();

            Mocker.Resolve<IRootFolderService>().Remove(_rootFolder.Id);
            DeleteFiles(files, DeleteMediaFileReason.MissingFromDisk);

            HoldsBackRssReleases(album).Should().BeFalse();
        }

        [Test]
        public void should_keep_holding_back_rss_releases_when_another_root_folder_is_removed()
        {
            var otherRootFolder = Db.Insert(Builder<RootFolder>.CreateNew()
                .With(r => r.Id = 0)
                .With(r => r.Path = _otherRootFolderPath)
                .Build());
            var (album, _, files) = GivenMonitoredAlbumWithFiles();

            DeleteFiles(files, DeleteMediaFileReason.MissingFromDisk);
            Mocker.Resolve<IRootFolderService>().Remove(otherRootFolder.Id);

            HoldsBackRssReleases(album).Should().BeTrue();
        }

        [Test]
        public void should_publish_the_unmonitored_album_after_handlers_that_broadcast_the_deleted_file()
        {
            // The API broadcasts the album each deleted file was loaded with, which is still monitored.
            var broadcaster = new AlbumBroadcaster();
            var handlers = Mocker.GetMock<IServiceFactory>();

            handlers.Setup(s => s.BuildAll<IHandle<TrackFileDeletedEvent>>())
                .Returns(() => new List<IHandle<TrackFileDeletedEvent>> { Subject, broadcaster });

            handlers.Setup(s => s.BuildAll<IHandle<AlbumEditedEvent>>())
                .Returns(new List<IHandle<AlbumEditedEvent>> { broadcaster });

            // Dispatch through a real event aggregator, which orders the handlers.
            var events = new EventAggregator(TestLogger, handlers.Object, Mocker.Resolve<IRuntimeInfo>());

            Mocker.GetMock<IEventAggregator>()
                .Setup(s => s.PublishEvent(It.IsAny<TrackFileDeletedEvent>()))
                .Callback<TrackFileDeletedEvent>(e => events.PublishEvent(e));

            Mocker.GetMock<IEventAggregator>()
                .Setup(s => s.PublishEvent(It.IsAny<AlbumEditedEvent>()))
                .Callback<AlbumEditedEvent>(e => events.PublishEvent(e));

            var (_, _, files) = GivenMonitoredAlbumWithFiles();
            var mediaFileService = Mocker.Resolve<IMediaFileService>();

            // As the API deletes them: loaded with their album, then deleted one at a time.
            foreach (var file in mediaFileService.Get(files.Select(f => f.Id)))
            {
                mediaFileService.Delete(file, DeleteMediaFileReason.Manual);
            }

            broadcaster.Monitored.Last().Should().BeFalse();
        }

        private class AlbumBroadcaster : IHandle<TrackFileDeletedEvent>, IHandle<AlbumEditedEvent>
        {
            public List<bool> Monitored { get; } = new List<bool>();

            public void Handle(TrackFileDeletedEvent message)
            {
                Monitored.Add(message.TrackFile.Album.Value.Monitored);
            }

            public void Handle(AlbumEditedEvent message)
            {
                Monitored.Add(message.Album.Monitored);
            }
        }
    }
}
