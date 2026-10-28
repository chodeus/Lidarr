using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.Extras.Files;
using NzbDrone.Core.Extras.Lyrics;
using NzbDrone.Core.Extras.Metadata;
using NzbDrone.Core.Extras.Metadata.Files;
using NzbDrone.Core.Extras.Others;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Music;
using NzbDrone.Core.Music.Events;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Extras
{
    // The producers of track file deletions through the real services; the bus hands their events to the real extra file services
    [TestFixture]
    public class ExtraFilesFollowTrackFilesFixture : DbTest<MediaFileService, TrackFile>
    {
        private Artist _artist;
        private Album _album;
        private List<TrackFile> _trackFiles;
        private List<string> _extras;
        private List<string> _recycled;
        private LyricFileService _lyricFileService;
        private OtherExtraFileService _otherExtraFileService;

        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<IDiskProvider>(new Mock<DiskProviderBase>(new FileSystem()) { CallBase = true }.Object);
            Mocker.SetConstant<IMediaFileRepository>(Mocker.Resolve<MediaFileRepository>());
            Mocker.SetConstant<IMediaFileService>(Subject);
            Mocker.SetConstant<ITrackRepository>(Mocker.Resolve<TrackRepository>());
            Mocker.SetConstant<ITrackService>(Mocker.Resolve<TrackService>());
            Mocker.SetConstant<IExtraFileRepository<LyricFile>>(Mocker.Resolve<LyricFileRepository>());
            Mocker.SetConstant<IExtraFileRepository<OtherExtraFile>>(Mocker.Resolve<OtherExtraFileRepository>());

            _recycled = new List<string>();
            Mocker.GetMock<IRecycleBinProvider>()
                  .Setup(s => s.DeleteFile(It.IsAny<string>(), It.IsAny<string>()))
                  .Callback<string, string>((path, subfolder) =>
                  {
                      _recycled.Add(Path.GetFileName(path));
                      File.Delete(path);
                  });

            _lyricFileService = Mocker.Resolve<LyricFileService>();
            _otherExtraFileService = Mocker.Resolve<OtherExtraFileService>();
            Mocker.GetMock<IEventAggregator>()
                  .Setup(s => s.PublishEvent(It.IsAny<TrackFileDeletedEvent>()))
                  .Callback<TrackFileDeletedEvent>(e =>
                  {
                      _lyricFileService.Handle(e);
                      _otherExtraFileService.Handle(e);
                  });

            var root = Path.Combine(TempFolder, "Music");
            var metadata = Db.Insert(Builder<ArtistMetadata>.CreateNew().With(a => a.Id = 0).Build());
            _artist = Db.Insert(Builder<Artist>.CreateNew().With(a => a.Id = 0).With(a => a.ArtistMetadataId = metadata.Id).With(a => a.Path = Path.Combine(root, "Artist")).Build());
            _album = Db.Insert(Builder<Album>.CreateNew().With(a => a.Id = 0).With(a => a.ArtistMetadataId = metadata.Id).Build());
            _album.Artist = _artist;
            var release = Db.Insert(Builder<AlbumRelease>.CreateNew().With(r => r.Id = 0).With(r => r.AlbumId = _album.Id).With(r => r.Monitored = true).Build());

            var folder = Path.Combine(_artist.Path, "Album");
            Directory.CreateDirectory(folder);
            _trackFiles = new List<TrackFile>();
            _extras = new List<string>();

            for (var i = 1; i <= 2; i++)
            {
                var path = Path.Combine(folder, $"0{i} - Song.flac");
                File.WriteAllText(path, "audio");
                var trackFile = Db.Insert(new TrackFile { AlbumId = _album.Id, Path = path, Quality = new QualityModel(Quality.FLAC), DateAdded = DateTime.UtcNow, Modified = DateTime.UtcNow });
                Db.Insert(Builder<Track>.CreateNew().With(t => t.Id = 0).With(t => t.ForeignTrackId = "track-" + i).With(t => t.AlbumReleaseId = release.Id).With(t => t.TrackFileId = trackFile.Id).Build());
                trackFile.Artist = new LazyLoaded<Artist>(_artist);
                _trackFiles.Add(trackFile);

                var lyric = Path.Combine("Album", $"0{i} - Song.lrc");
                var log = Path.Combine("Album", $"0{i} - Song.log");
                File.WriteAllText(Path.Combine(_artist.Path, lyric), "[00:01.00] lyric");
                File.WriteAllText(Path.Combine(_artist.Path, log), "rip log");
                Db.Insert(new LyricFile { ArtistId = _artist.Id, AlbumId = _album.Id, TrackFileId = trackFile.Id, RelativePath = lyric, Extension = ".lrc", Added = DateTime.UtcNow, LastUpdated = DateTime.UtcNow });
                Db.Insert(new OtherExtraFile { ArtistId = _artist.Id, AlbumId = _album.Id, TrackFileId = trackFile.Id, RelativePath = log, Extension = ".log", Added = DateTime.UtcNow, LastUpdated = DateTime.UtcNow });
                _extras.Add(Path.Combine(_artist.Path, lyric));
                _extras.Add(Path.Combine(_artist.Path, log));
            }
        }

        private int ExtraRows => Db.All<LyricFile>().Count + Db.All<OtherExtraFile>().Count;

        // Settings only drop Lidarr's rows for the folder; the audio stays, and so must what sits beside it
        [Test]
        public void should_keep_the_extra_files_on_disk_when_their_root_folder_is_removed()
        {
            Subject.HandleAsync(new ModelEvent<RootFolder>(new RootFolder { Id = 1, Path = Path.Combine(TempFolder, "Music") }, ModelAction.Deleted));

            new { Recycled = _recycled, OnDisk = _extras.Count(File.Exists), Rows = ExtraRows }
                .Should().BeEquivalentTo(new { Recycled = new List<string>(), OnDisk = 4, Rows = 0 });
        }

        // A tagger renamed the audio outside Lidarr; the scan drops the old rows, and what came with the files stays beside them
        [Test]
        public void should_keep_the_extra_files_on_disk_when_a_scan_finds_their_track_files_gone()
        {
            var renamed = _trackFiles.Select(f =>
            {
                var path = Path.Combine(Path.GetDirectoryName(f.Path), "Artist - " + Path.GetFileName(f.Path));
                File.Move(f.Path, path);
                return path;
            }).ToList();

            Mocker.Resolve<MediaFileTableCleanupService>().Clean(_artist.Path, renamed);

            new { Recycled = _recycled, OnDisk = _extras.Count(File.Exists), Rows = ExtraRows }
                .Should().BeEquivalentTo(new { Recycled = new List<string>(), OnDisk = 4, Rows = 0 });
        }

        [Test]
        public void should_recycle_a_track_files_extras_when_a_user_deletes_it()
        {
            Mocker.Resolve<MediaFileDeletionService>().DeleteTrackFile(_trackFiles[0]);

            _recycled.Should().BeEquivalentTo("01 - Song.flac", "01 - Song.lrc", "01 - Song.log");
            ExtraRows.Should().Be(2);
        }

        // The event aggregator runs synchronous handlers first, so the extras are gone before the asynchronous empty-folder check
        [Test]
        public void should_take_an_albums_extras_away_synchronously()
        {
            typeof(OtherExtraFileService).Should().Implement<IHandle<AlbumDeletedEvent>>();
            typeof(IHandleAsync<AlbumDeletedEvent>).IsAssignableFrom(typeof(OtherExtraFileService)).Should().BeFalse();
        }

        // A file on an unmonitored release isn't among the files the album deletion removes, so its extras stay with it
        [Test]
        public void should_recycle_only_the_extras_of_the_track_files_being_deleted()
        {
            var deleted = new AlbumDeletedEvent(_album, true, false) { TrackFilesToDelete = new List<TrackFile> { _trackFiles[0] } };

            _lyricFileService.Handle(deleted);
            _otherExtraFileService.Handle(deleted);

            _recycled.Should().BeEquivalentTo("01 - Song.lrc", "01 - Song.log");
            ExtraRows.Should().Be(2);
        }

        [Test]
        public void should_recycle_the_other_extras_when_one_cannot_be()
        {
            Mocker.GetMock<IRecycleBinProvider>()
                  .Setup(s => s.DeleteFile(It.Is<string>(p => p.EndsWith("01 - Song.lrc")), It.IsAny<string>()))
                  .Throws(new IOException("in use"));
            var deleted = new AlbumDeletedEvent(_album, true, false) { TrackFilesToDelete = Subject.GetFilesByAlbum(_album.Id) };

            _lyricFileService.Handle(deleted);
            _otherExtraFileService.Handle(deleted);

            _recycled.Should().BeEquivalentTo("02 - Song.lrc", "01 - Song.log", "02 - Song.log");
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_move_the_album_with_the_track_file_when_relinking()
        {
            _otherExtraFileService.Relink(_trackFiles[0].Id, new TrackFile { Id = 99, AlbumId = 42 });

            Db.All<OtherExtraFile>().Where(e => e.TrackFileId == 99).Select(e => e.AlbumId).Should().Equal(42);
        }

        [TestCase(true, 0)]
        [TestCase(false, 4)]
        public void should_recycle_the_albums_extras_only_when_the_album_is_deleted_with_its_files(bool deleteFiles, int left)
        {
            var deleted = new AlbumDeletedEvent(_album, deleteFiles, false) { TrackFilesToDelete = Subject.GetFilesByAlbum(_album.Id) };

            _lyricFileService.Handle(deleted);
            _otherExtraFileService.Handle(deleted);

            new { OnDisk = _extras.Count(File.Exists), Rows = ExtraRows }.Should().BeEquivalentTo(new { OnDisk = left, Rows = left });
        }

        // An album's own file, such as its cover, has no track file; nothing says which files the album deletes
        [Test]
        public void should_recycle_the_albums_own_extras_when_it_is_deleted_with_its_files()
        {
            Mocker.SetConstant<IExtraFileRepository<MetadataFile>>(Mocker.Resolve<MetadataFileRepository>());
            var cover = Path.Combine("Album", "cover.jpg");
            File.WriteAllText(Path.Combine(_artist.Path, cover), "image");
            Db.Insert(new MetadataFile { ArtistId = _artist.Id, AlbumId = _album.Id, TrackFileId = null, RelativePath = cover, Extension = ".jpg", Consumer = "XbmcMetadata", Type = MetadataType.AlbumImage, Added = DateTime.UtcNow, LastUpdated = DateTime.UtcNow });

            Mocker.Resolve<MetadataFileService>().Handle(new AlbumDeletedEvent(_album, true, false));

            new { Recycled = _recycled, Rows = Db.All<MetadataFile>().Count }.Should().BeEquivalentTo(new { Recycled = new[] { "cover.jpg" }, Rows = 0 });
        }

        // Rows develop stored before their track files had ids; a name only counts beside its track file
        [Test]
        public void should_recycle_an_unlinked_extra_only_when_it_is_named_after_a_track_file_being_deleted()
        {
            Directory.CreateDirectory(Path.Combine(_artist.Path, "Album", "Scans"));

            foreach (var relativePath in new[] { Path.Combine("Album", "01 - Song.txt"), Path.Combine("Album", "02 - Song.txt"), Path.Combine("Album", "Scans", "01 - Song.txt") })
            {
                File.WriteAllText(Path.Combine(_artist.Path, relativePath), "lyric");
                Db.Insert(new OtherExtraFile { ArtistId = _artist.Id, AlbumId = _album.Id, TrackFileId = 0, RelativePath = relativePath, Extension = ".txt", Added = DateTime.UtcNow, LastUpdated = DateTime.UtcNow });
            }

            _otherExtraFileService.Handle(new AlbumDeletedEvent(_album, true, false) { TrackFilesToDelete = new List<TrackFile> { _trackFiles[0] } });

            new
            {
                Recycled = _recycled,
                KeptOnDisk = File.Exists(Path.Combine(_artist.Path, "Album", "02 - Song.txt")) && File.Exists(Path.Combine(_artist.Path, "Album", "Scans", "01 - Song.txt")),
                Rows = Db.All<OtherExtraFile>().Select(e => Path.GetFileName(e.RelativePath)).ToList()
            }.Should().BeEquivalentTo(new { Recycled = new[] { "01 - Song.log", "01 - Song.txt" }, KeptOnDisk = true, Rows = new[] { "02 - Song.log" } });
        }

        [Test]
        public void should_keep_an_unlinked_extra_on_disk_when_its_name_also_fits_a_track_file_the_album_keeps()
        {
            var kept = Path.Combine(_artist.Path, "Album", "01 - Song.wav");
            File.WriteAllText(kept, "audio");
            Db.Insert(new TrackFile { AlbumId = _album.Id, Path = kept, Quality = new QualityModel(Quality.FLAC), DateAdded = DateTime.UtcNow, Modified = DateTime.UtcNow });
            var lyric = Path.Combine("Album", "01 - Song.txt");
            File.WriteAllText(Path.Combine(_artist.Path, lyric), "lyric");
            Db.Insert(new OtherExtraFile { ArtistId = _artist.Id, AlbumId = _album.Id, TrackFileId = 0, RelativePath = lyric, Extension = ".txt", Added = DateTime.UtcNow, LastUpdated = DateTime.UtcNow });

            _otherExtraFileService.Handle(new AlbumDeletedEvent(_album, true, false) { TrackFilesToDelete = new List<TrackFile> { _trackFiles[0] } });

            new { Recycled = _recycled, KeptOnDisk = File.Exists(Path.Combine(_artist.Path, lyric)) }.Should().BeEquivalentTo(new { Recycled = new[] { "01 - Song.log" }, KeptOnDisk = true });
        }

        // Develop's constructor gives no track files to tell an unlinked extra's owner by
        [Test]
        public void should_keep_an_unlinked_extra_on_disk_and_drop_its_row_without_the_media_file_service()
        {
            var lyric = Path.Combine("Album", "01 - Song.txt");
            File.WriteAllText(Path.Combine(_artist.Path, lyric), "lyric");
            Db.Insert(new OtherExtraFile { ArtistId = _artist.Id, AlbumId = _album.Id, TrackFileId = 0, RelativePath = lyric, Extension = ".txt", Added = DateTime.UtcNow, LastUpdated = DateTime.UtcNow });
            var service = new OtherExtraFileService(Mocker.Resolve<IExtraFileRepository<OtherExtraFile>>(), Mocker.Resolve<IArtistService>(), Mocker.Resolve<IDiskProvider>(), Mocker.Resolve<IRecycleBinProvider>(), TestLogger);

            service.Handle(new AlbumDeletedEvent(_album, true, false) { TrackFilesToDelete = new List<TrackFile> { _trackFiles[0] } });

            new { Recycled = _recycled, KeptOnDisk = File.Exists(Path.Combine(_artist.Path, lyric)), Unlinked = Db.All<OtherExtraFile>().Count(e => e.TrackFileId == 0) }
                .Should().BeEquivalentTo(new { Recycled = new[] { "01 - Song.log" }, KeptOnDisk = true, Unlinked = 0 });
        }
    }
}
