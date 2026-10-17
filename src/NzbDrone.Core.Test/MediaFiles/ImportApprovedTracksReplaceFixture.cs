using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Download;
using NzbDrone.Core.History;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.TrackImport;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles
{
    // Replacing an album's files on download import, over real files in a temp folder
    [TestFixture]
    public class ImportApprovedTracksReplaceFixture : CoreTest<ImportApprovedTracks>
    {
        private Artist _artist;
        private Album _album;
        private AlbumRelease _oldRelease;
        private AlbumRelease _newRelease;
        private string _albumFolder;
        private List<TrackFile> _rows;
        private List<int> _deletedIds;

        // DiskProviderBase's own file and folder operations on the real file system
        private class RealDiskProvider : DiskProviderBase
        {
            public RealDiskProvider()
                : base(new FileSystem())
            {
            }

            public override long? GetAvailableSpace(string path) => null;
            public override void InheritFolderPermissions(string filename)
            {
            }

            public override void SetEveryonePermissions(string filename)
            {
            }

            public override void SetFilePermissions(string path, string mask, string group)
            {
            }

            public override void SetPermissions(string path, string mask, string group)
            {
            }

            public override void CopyPermissions(string sourcePath, string targetPath)
            {
            }

            public override long? GetTotalSize(string path) => null;
            public override bool TryCreateHardLink(string source, string destination) => false;
        }

        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<IDiskProvider>(new RealDiskProvider());

            _artist = new Artist
            {
                Id = 1,
                Path = Path.Combine(TempFolder, "Music", "Artist"),
                Metadata = new ArtistMetadata { Name = "Artist" },
                QualityProfile = new QualityProfile { Items = Qualities.QualityFixture.GetDefaultQualities() }
            };

            _albumFolder = Path.Combine(_artist.Path, "Album");
            Directory.CreateDirectory(_albumFolder);

            _album = new Album { Id = 1, ForeignAlbumId = "album-1", Artist = _artist };
            _oldRelease = new AlbumRelease { Id = 10, AlbumId = 1, Monitored = true, Album = _album };
            _newRelease = new AlbumRelease { Id = 11, AlbumId = 1, Monitored = false, Album = _album };
            _album.AlbumReleases = new List<AlbumRelease> { _oldRelease, _newRelease };

            _rows = Enumerable.Range(1, 3).Select(i => new TrackFile
            {
                Id = i,
                AlbumId = 1,
                Path = Path.Combine(_albumFolder, $"0{i} - Song.mp3"),
                Quality = new QualityModel(Quality.MP3_320)
            }).ToList();
            _rows.ForEach(f => File.WriteAllText(f.Path, "old " + f.Id));
            _deletedIds = new List<int>();

            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.GetFilesByAlbum(1))
                  .Returns(() => _rows.Where(f => !_deletedIds.Contains(f.Id)).ToList());

            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.Delete(It.IsAny<TrackFile>(), It.IsAny<DeleteMediaFileReason>()))
                  .Callback<TrackFile, DeleteMediaFileReason>((f, r) => _deletedIds.Add(f.Id));

            // The bin takes the file away, as RecycleBinProvider's move does
            Mocker.GetMock<IRecycleBinProvider>()
                  .Setup(s => s.DeleteFile(It.IsAny<string>(), It.IsAny<string>()))
                  .Callback<string, string>((path, subfolder) => File.Delete(path));

            Mocker.GetMock<IHistoryService>()
                  .Setup(s => s.FindByDownloadId(It.IsAny<string>()))
                  .Returns(new List<EntityHistory>());

            Mocker.GetMock<IReleaseService>()
                  .Setup(s => s.SetMonitored(It.IsAny<AlbumRelease>()))
                  .Returns<AlbumRelease>(r => _album.AlbumReleases.Value.Select(x =>
                  {
                      x.Monitored = x.Id == r.Id;
                      return x;
                  }).ToList());
        }

        private ImportDecision<LocalTrack> Incoming(AlbumRelease release, Track track)
        {
            return new ImportDecision<LocalTrack>(new LocalTrack
            {
                Artist = _artist,
                Album = _album,
                Release = release,
                Tracks = new List<Track> { track },
                Path = Path.Combine(TempFolder, "downloads", "Single [WEB FLAC]", "01 - Song.flac"),
                Quality = new QualityModel(Quality.FLAC),
                FileTrackInfo = new ParsedTrackInfo()
            });
        }

        private List<ImportResult> Import(ImportDecision<LocalTrack> decision)
        {
            return Subject.Import(new List<ImportDecision<LocalTrack>> { decision }, true, new DownloadClientItem { DownloadId = "dl-1", OutputPath = new OsPath(Path.Combine(TempFolder, "downloads", "Single [WEB FLAC]")) });
        }

        private void GivenNewFileLandsAt(string fileName)
        {
            Mocker.GetMock<IUpgradeMediaFiles>()
                  .Setup(s => s.UpgradeTrackFile(It.IsAny<TrackFile>(), It.IsAny<LocalTrack>(), It.IsAny<bool>()))
                  .Returns<TrackFile, LocalTrack, bool>((f, l, c) =>
                  {
                      var destination = Path.Combine(_albumFolder, fileName);

                      // Moving onto a file that is still there fails, as TrackFileMovingService's transfer does
                      if (File.Exists(destination))
                      {
                          throw new DestinationAlreadyExistsException(destination);
                      }

                      File.WriteAllText(destination, "new");
                      f.Path = destination;
                      return new TrackFileMoveResult { TrackFile = f };
                  });
        }

        private string Aside(TrackFile file)
        {
            return Path.Combine(_albumFolder, ".lidarr-replaced", Path.GetFileName(file.Path));
        }

        [Test]
        public void should_keep_the_previous_release_and_its_files_when_nothing_of_the_new_release_imports()
        {
            var originals = _rows.Select(f => f.Path).ToList();
            Mocker.GetMock<IUpgradeMediaFiles>()
                  .Setup(s => s.UpgradeTrackFile(It.IsAny<TrackFile>(), It.IsAny<LocalTrack>(), It.IsAny<bool>()))
                  .Throws(new DestinationAlreadyExistsException(Path.Combine(_albumFolder, "01 - Song.flac")));

            var results = Import(Incoming(_newRelease, new Track { Id = 101, AlbumReleaseId = 11 }));

            ExceptionVerification.ExpectedWarns(2);
            results.Should().OnlyContain(r => r.Result != ImportResultType.Imported);
            originals.Should().OnlyContain(p => File.Exists(p));
            _rows.Select(f => f.Path).Should().Equal(originals);
            Directory.Exists(Path.Combine(_albumFolder, ".lidarr-replaced")).Should().BeFalse();
            _oldRelease.Monitored.Should().BeTrue();
            _newRelease.Monitored.Should().BeFalse();
            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(It.IsAny<TrackFile>(), It.IsAny<DeleteMediaFileReason>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Update(It.Is<List<TrackFile>>(l => l.Count == 3 && l.All(f => originals.Contains(f.Path)))), Times.AtLeastOnce());
        }

        [Test]
        public void should_recycle_the_previous_files_once_a_track_of_the_new_release_imports()
        {
            var originals = _rows.Select(f => f.Path).ToList();
            GivenNewFileLandsAt("01 - Single.flac");

            var results = Import(Incoming(_newRelease, new Track { Id = 101, AlbumReleaseId = 11 }));

            ExceptionVerification.ExpectedWarns(0);
            results.Should().ContainSingle(r => r.Result == ImportResultType.Imported);
            _newRelease.Monitored.Should().BeTrue();

            foreach (var original in originals)
            {
                Mocker.GetMock<IRecycleBinProvider>()
                      .Verify(v => v.DeleteFile(Path.Combine(_albumFolder, ".lidarr-replaced", Path.GetFileName(original)), Path.Combine("Artist", "Album")), Times.Once());
            }

            _deletedIds.Should().BeEquivalentTo(new[] { 1, 2, 3 });
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Update(It.Is<List<TrackFile>>(l => l.Count == 3)), Times.Once(), "the rows followed the files aside");
            _rows.Select(f => f.Path).Should().Equal(originals, "history and events see the files where they were");
            Directory.Exists(Path.Combine(_albumFolder, ".lidarr-replaced")).Should().BeFalse();
        }

        [Test]
        public void should_free_the_previous_files_paths_for_the_new_files()
        {
            GivenNewFileLandsAt("01 - Song.mp3");

            var results = Import(Incoming(_newRelease, new Track { Id = 101, AlbumReleaseId = 11 }));

            results.Should().ContainSingle(r => r.Result == ImportResultType.Imported);
            File.ReadAllText(Path.Combine(_albumFolder, "01 - Song.mp3")).Should().Be("new");
        }

        [Test]
        public void should_put_everything_back_and_write_nothing_when_moving_a_file_aside_fails()
        {
            // The second disc's hidden folder can't be created: a plain file has its name
            var secondDisc = Path.Combine(_albumFolder, "CD2");
            Directory.CreateDirectory(secondDisc);
            File.Move(_rows[1].Path, Path.Combine(secondDisc, "02 - Song.mp3"));
            _rows[1].Path = Path.Combine(secondDisc, "02 - Song.mp3");
            File.WriteAllText(Path.Combine(secondDisc, ".lidarr-replaced"), "not a folder");
            var originals = _rows.Select(f => f.Path).ToList();

            Assert.Catch<IOException>(() => Import(Incoming(_newRelease, new Track { Id = 101, AlbumReleaseId = 11 })));

            originals.Should().OnlyContain(p => File.Exists(p));
            File.ReadAllText(originals[0]).Should().Be("old 1");
            _rows.Select(f => f.Path).Should().Equal(originals);
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Update(It.IsAny<List<TrackFile>>()), Times.Never());
            Mocker.GetMock<IReleaseService>().Verify(v => v.SetMonitored(It.IsAny<AlbumRelease>()), Times.Never());
            Mocker.GetMock<IUpgradeMediaFiles>().Verify(v => v.UpgradeTrackFile(It.IsAny<TrackFile>(), It.IsAny<LocalTrack>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void should_not_trip_over_a_file_an_interrupted_import_left_aside()
        {
            var originals = _rows.Select(f => f.Path).ToList();
            Directory.CreateDirectory(Path.Combine(_albumFolder, ".lidarr-replaced"));
            File.WriteAllText(Aside(_rows[1]), "left over");
            Mocker.GetMock<IUpgradeMediaFiles>()
                  .Setup(s => s.UpgradeTrackFile(It.IsAny<TrackFile>(), It.IsAny<LocalTrack>(), It.IsAny<bool>()))
                  .Throws(new DestinationAlreadyExistsException(Path.Combine(_albumFolder, "01 - Song.flac")));

            Import(Incoming(_newRelease, new Track { Id = 101, AlbumReleaseId = 11 }));

            ExceptionVerification.ExpectedWarns(2);
            originals.Should().OnlyContain(p => File.Exists(p));
            File.ReadAllText(originals[1]).Should().Be("old 2");
            File.ReadAllText(Aside(_rows[1])).Should().Be("left over");
        }

        [Test]
        public void should_upgrade_the_same_release_through_the_real_upgrade_service()
        {
            Mocker.SetConstant<IUpgradeMediaFiles>(Mocker.Resolve<UpgradeMediaFileService>());
            Mocker.GetMock<IMoveTrackFiles>()
                  .Setup(s => s.MoveTrackFile(It.IsAny<TrackFile>(), It.IsAny<LocalTrack>()))
                  .Returns<TrackFile, LocalTrack>((f, l) =>
                  {
                      f.Path = Path.Combine(_albumFolder, "01 - Song.flac");
                      File.WriteAllText(f.Path, "new");
                      return f;
                  });

            // Import specs load the track's file as its own copy, still at the original path
            var loaded = new TrackFile { Id = 1, AlbumId = 1, Path = _rows[0].Path, Quality = _rows[0].Quality };
            var track = new Track { Id = 1, AlbumReleaseId = 10, TrackFileId = 1, TrackFile = loaded };

            var results = Import(Incoming(_oldRelease, track));

            // LoggingTest only fails unexpected warns in Debug builds, so count them here
            ExceptionVerification.ExpectedWarns(0);
            results.Should().ContainSingle(r => r.Result == ImportResultType.Imported);

            // The upgrade recycles its own file from where it waits; the album's other files go once the import worked
            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFile(Aside(_rows[0]), It.IsAny<string>()), Times.Once());
            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Exactly(3));
            _deletedIds.Should().BeEquivalentTo(new[] { 1, 2, 3 });
        }
    }
}
