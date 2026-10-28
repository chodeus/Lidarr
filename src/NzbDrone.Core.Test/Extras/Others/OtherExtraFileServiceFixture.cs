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
using NzbDrone.Core.Extras.Files;
using NzbDrone.Core.Extras.Others;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Music;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Extras.Others
{
    // The real repositories over the test database
    [TestFixture]
    public class OtherExtraFileServiceFixture : DbTest<OtherExtraFileService, OtherExtraFile>
    {
        private Artist _artist;
        private List<TrackFile> _trackFiles;
        private string _releaseFile;
        private List<string> _recycled;

        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<IDiskProvider>(new Mock<DiskProviderBase>(new FileSystem()) { CallBase = true }.Object);
            Mocker.SetConstant<IExtraFileRepository<OtherExtraFile>>(Mocker.Resolve<OtherExtraFileRepository>());
            Mocker.SetConstant<IMediaFileRepository>(Mocker.Resolve<MediaFileRepository>());
            Mocker.SetConstant<IMediaFileService>(Mocker.Resolve<MediaFileService>());

            _recycled = new List<string>();
            Mocker.GetMock<IRecycleBinProvider>()
                  .Setup(s => s.DeleteFile(It.IsAny<string>(), It.IsAny<string>()))
                  .Callback<string, string>((path, subfolder) =>
                  {
                      _recycled.Add(Path.GetFileName(path));
                      File.Delete(path);
                  });

            _artist = Builder<Artist>.CreateNew().With(a => a.Id = 1).With(a => a.Path = Path.Combine(TempFolder, "Music", "Artist")).Build();
            var folder = Path.Combine(_artist.Path, "Album");
            Directory.CreateDirectory(folder);

            // GetFilesByAlbum counts the files linked to the monitored release's tracks
            var release = Db.Insert(Builder<AlbumRelease>.CreateNew().With(r => r.Id = 0).With(r => r.AlbumId = 7).With(r => r.Monitored = true).Build());
            _trackFiles = Enumerable.Range(1, 2).Select(i => Db.Insert(new TrackFile { AlbumId = 7, Path = Path.Combine(folder, $"0{i} - Song.flac"), Quality = new QualityModel(Quality.FLAC) })).ToList();
            _trackFiles.ForEach(f => Db.Insert(Builder<Track>.CreateNew().With(t => t.Id = 0).With(t => t.ForeignTrackId = "track-" + f.Id).With(t => t.AlbumReleaseId = release.Id).With(t => t.TrackFileId = f.Id).Build()));
            _trackFiles.ForEach(f => f.Artist = new LazyLoaded<Artist>(_artist));

            _releaseFile = Path.Combine(folder, "Album.log");
            File.WriteAllText(_releaseFile, "rip log");
            Db.Insert(new OtherExtraFile { ArtistId = _artist.Id, AlbumId = 7, TrackFileId = null, RelativePath = Path.Combine("Album", "Album.log"), Extension = ".log", Added = DateTime.UtcNow, LastUpdated = DateTime.UtcNow });
        }

        private void Delete(TrackFile trackFile, DeleteMediaFileReason reason)
        {
            Db.Delete(trackFile);
            Subject.Handle(new TrackFileDeletedEvent(trackFile, reason));
        }

        [Test]
        public void should_recycle_only_the_albums_own_release_files()
        {
            File.WriteAllText(Path.Combine(_artist.Path, "Album", "01 - Song.lrc"), "lyric");
            Db.Insert(new OtherExtraFile { ArtistId = _artist.Id, AlbumId = 7, TrackFileId = _trackFiles[0].Id, RelativePath = Path.Combine("Album", "01 - Song.lrc"), Extension = ".lrc", Added = DateTime.UtcNow, LastUpdated = DateTime.UtcNow });
            Db.Insert(new OtherExtraFile { ArtistId = _artist.Id, AlbumId = 8, TrackFileId = null, RelativePath = Path.Combine("Other", "Other.log"), Extension = ".log", Added = DateTime.UtcNow, LastUpdated = DateTime.UtcNow });

            Subject.DeleteReleaseFiles(_artist, 7);

            _recycled.Should().Equal("Album.log");
            AllStoredModels.Select(f => Path.GetFileName(f.RelativePath)).Should().BeEquivalentTo("01 - Song.lrc", "Other.log");
        }

        // Only a replacing import or the album's own deletion takes a release's files away
        [Test]
        public void should_keep_the_release_files_when_a_user_removes_the_albums_last_track_file()
        {
            _trackFiles.ForEach(f => Delete(f, DeleteMediaFileReason.Manual));

            _recycled.Should().BeEmpty();
            File.Exists(_releaseFile).Should().BeTrue();
            AllStoredModels.Should().HaveCount(1);
        }

        [TestCase(true, 0)]
        [TestCase(false, 1)]
        public void should_recycle_the_release_files_only_when_the_album_is_deleted_with_its_files(bool deleteFiles, int left)
        {
            var album = new Album { Id = 7, Artist = new LazyLoaded<Artist>(_artist) };

            Subject.Handle(new NzbDrone.Core.Music.Events.AlbumDeletedEvent(album, deleteFiles, false));

            new { OnDisk = File.Exists(_releaseFile), Rows = AllStoredModels.Count }.Should().BeEquivalentTo(new { OnDisk = left == 1, Rows = left });
        }
    }
}
