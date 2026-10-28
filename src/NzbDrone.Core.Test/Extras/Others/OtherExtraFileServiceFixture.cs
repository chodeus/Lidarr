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

        [TestCase(DeleteMediaFileReason.Manual)]
        [TestCase(DeleteMediaFileReason.MissingFromDisk)]
        public void should_recycle_the_release_files_once_the_albums_last_track_file_goes(DeleteMediaFileReason reason)
        {
            _trackFiles.ForEach(f => Delete(f, reason));

            _recycled.Should().Equal("Album.log");
            AllStoredModels.Should().BeEmpty();
        }

        [Test]
        public void should_keep_the_release_files_while_the_album_has_a_track_file()
        {
            Delete(_trackFiles[0], DeleteMediaFileReason.Manual);

            _recycled.Should().BeEmpty();
            AllStoredModels.Should().HaveCount(1);
        }

        // An import deletes and adds the album's files and replaces its release files itself; a cleanup leaves the files on disk
        [TestCase(DeleteMediaFileReason.Upgrade)]
        [TestCase(DeleteMediaFileReason.ManualOverride)]
        [TestCase(DeleteMediaFileReason.NoLinkedEpisodes)]
        public void should_leave_the_release_files_when_the_album_loses_its_files_another_way(DeleteMediaFileReason reason)
        {
            _trackFiles.ForEach(f => Delete(f, reason));

            _recycled.Should().BeEmpty();
            File.Exists(_releaseFile).Should().BeTrue();
            AllStoredModels.Should().HaveCount(1);
        }
    }
}
