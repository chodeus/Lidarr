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
using NzbDrone.Core.Extras.Others;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Music;
using NzbDrone.Core.Music.Events;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;

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

        [Test]
        public void should_recycle_a_track_files_extras_when_a_user_deletes_it()
        {
            Mocker.Resolve<MediaFileDeletionService>().DeleteTrackFile(_trackFiles[0]);

            _recycled.Should().BeEquivalentTo("01 - Song.flac", "01 - Song.lrc", "01 - Song.log");
            ExtraRows.Should().Be(2);
        }

        [TestCase(true, 0)]
        [TestCase(false, 4)]
        public void should_recycle_the_albums_extras_only_when_the_album_is_deleted_with_its_files(bool deleteFiles, int left)
        {
            var deleted = new AlbumDeletedEvent(_album, deleteFiles, false) { TrackFilesToDelete = Subject.GetFilesByAlbum(_album.Id) };

            _lyricFileService.HandleAsync(deleted);
            _otherExtraFileService.HandleAsync(deleted);

            new { OnDisk = _extras.Count(File.Exists), Rows = ExtraRows }.Should().BeEquivalentTo(new { OnDisk = left, Rows = left });
        }
    }
}
