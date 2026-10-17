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
using NzbDrone.Core.Download;
using NzbDrone.Core.History;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.MediaFiles.TrackImport;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles
{
    // Replacing an album on a download import: the real media file, track and release services on the test database, over real files
    [TestFixture]
    public class ImportApprovedTracksRollbackFixture : DbTest<ImportApprovedTracks, TrackFile>
    {
        private Artist _artist;
        private ArtistMetadata _metadata;
        private List<(string Path, string Subfolder, string Content)> _recycled;

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

        private class TestAlbum
        {
            public Album Album { get; set; }
            public AlbumRelease OldRelease { get; set; }
            public AlbumRelease NewRelease { get; set; }
            public string Folder { get; set; }
            public List<string> OldPaths { get; set; }
            public List<Track> OldTracks { get; set; }
            public Track NewTrack { get; set; }
        }

        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<IDiskProvider>(new RealDiskProvider());
            Mocker.SetConstant<IMediaFileRepository>(Mocker.Resolve<MediaFileRepository>());
            Mocker.SetConstant<IMediaFileService>(Mocker.Resolve<MediaFileService>());
            Mocker.SetConstant<IReleaseRepository>(Mocker.Resolve<ReleaseRepository>());
            Mocker.SetConstant<IReleaseService>(Mocker.Resolve<ReleaseService>());
            Mocker.SetConstant<ITrackRepository>(Mocker.Resolve<TrackRepository>());

            var trackService = Mocker.Resolve<TrackService>();
            Mocker.SetConstant<ITrackService>(trackService);

            // MediaFileService.Delete publishes this; TrackService unlinks the file's tracks, as in the app
            Mocker.GetMock<IEventAggregator>()
                  .Setup(s => s.PublishEvent(It.IsAny<TrackFileDeletedEvent>()))
                  .Callback<TrackFileDeletedEvent>(e => trackService.Handle(e));

            _recycled = new List<(string, string, string)>();
            Mocker.GetMock<IRecycleBinProvider>()
                  .Setup(s => s.DeleteFile(It.IsAny<string>(), It.IsAny<string>()))
                  .Callback<string, string>((path, subfolder) =>
                  {
                      _recycled.Add((path, subfolder, File.ReadAllText(path)));
                      File.Delete(path);
                  });

            Mocker.GetMock<IHistoryService>()
                  .Setup(s => s.FindByDownloadId(It.IsAny<string>()))
                  .Returns(new List<EntityHistory>());

            _metadata = Builder<ArtistMetadata>.CreateNew().With(a => a.Id = 0).Build();
            Db.Insert(_metadata);
            _artist = Builder<Artist>.CreateNew()
                                     .With(a => a.Id = 0)
                                     .With(a => a.ArtistMetadataId = _metadata.Id)
                                     .With(a => a.Path = Path.Combine(TempFolder, "Music", "Artist"))
                                     .Build();
            Db.Insert(_artist);
            _artist.Metadata = _metadata;
            _artist.QualityProfile = new QualityProfile { Items = Qualities.QualityFixture.GetDefaultQualities() };
        }

        private TestAlbum GivenAlbum(string name, string subfolder = null, int files = 3)
        {
            var album = Builder<Album>.CreateNew().With(a => a.Id = 0).With(a => a.ArtistMetadataId = _metadata.Id).With(a => a.ForeignAlbumId = "album-" + name).Build();
            Db.Insert(album);

            var oldRelease = Builder<AlbumRelease>.CreateNew().With(r => r.Id = 0).With(r => r.AlbumId = album.Id).With(r => r.Monitored = true).With(r => r.ForeignReleaseId = name + "-old").Build();
            var newRelease = Builder<AlbumRelease>.CreateNew().With(r => r.Id = 0).With(r => r.AlbumId = album.Id).With(r => r.Monitored = false).With(r => r.ForeignReleaseId = name + "-new").Build();
            Db.Insert(oldRelease);
            Db.Insert(newRelease);
            album.AlbumReleases = new List<AlbumRelease> { oldRelease, newRelease };
            album.Artist = _artist;

            var folder = subfolder == null ? Path.Combine(_artist.Path, name) : Path.Combine(_artist.Path, name, subfolder);
            Directory.CreateDirectory(folder);

            var test = new TestAlbum { Album = album, OldRelease = oldRelease, NewRelease = newRelease, Folder = folder, OldPaths = new List<string>(), OldTracks = new List<Track>() };

            foreach (var i in Enumerable.Range(1, files))
            {
                var file = new TrackFile { AlbumId = album.Id, Path = Path.Combine(folder, $"0{i} - Song.mp3"), Quality = new QualityModel(Quality.MP3_320), Size = 100 + i };
                File.WriteAllText(file.Path, "old " + i);
                Db.Insert(file);

                var track = Builder<Track>.CreateNew().With(t => t.Id = 0).With(t => t.ForeignTrackId = $"{name}-old-{i}").With(t => t.AlbumReleaseId = oldRelease.Id).With(t => t.TrackFileId = file.Id).Build();
                Db.Insert(track);
                test.OldPaths.Add(file.Path);
                test.OldTracks.Add(track);
            }

            test.NewTrack = Builder<Track>.CreateNew().With(t => t.Id = 0).With(t => t.ForeignTrackId = name + "-new-1").With(t => t.AlbumReleaseId = newRelease.Id).With(t => t.TrackFileId = 0).Build();
            Db.Insert(test.NewTrack);

            return test;
        }

        private ImportDecision<LocalTrack> Incoming(TestAlbum album, AlbumRelease release, Track track)
        {
            return new ImportDecision<LocalTrack>(new LocalTrack
            {
                Artist = _artist,
                Album = album.Album,
                Release = release,
                Tracks = new List<Track> { track },
                Path = Path.Combine(TempFolder, "downloads", "Single [WEB FLAC]", "01 - Single.flac"),
                Quality = new QualityModel(Quality.FLAC),
                FileTrackInfo = new ParsedTrackInfo()
            });
        }

        private List<ImportResult> Import(params ImportDecision<LocalTrack>[] decisions)
        {
            return Subject.Import(decisions.ToList(), true, new DownloadClientItem { DownloadId = "dl-1" });
        }

        private void GivenNewFileLandsAs(string fileName)
        {
            Mocker.GetMock<IUpgradeMediaFiles>()
                  .Setup(s => s.UpgradeTrackFile(It.IsAny<TrackFile>(), It.IsAny<LocalTrack>(), It.IsAny<bool>()))
                  .Returns<TrackFile, LocalTrack, bool>((f, l, c) =>
                  {
                      f.Path = Path.Combine(_artist.Path, l.Album.ForeignAlbumId.Replace("album-", ""), fileName);
                      File.WriteAllText(f.Path, "new");
                      return new TrackFileMoveResult { TrackFile = f };
                  });
        }

        private void GivenNothingImports()
        {
            Mocker.GetMock<IUpgradeMediaFiles>()
                  .Setup(s => s.UpgradeTrackFile(It.IsAny<TrackFile>(), It.IsAny<LocalTrack>(), It.IsAny<bool>()))
                  .Throws(new DestinationAlreadyExistsException("destination exists"));
        }

        // The album is back as it was: its files and their contents, a row per file linked to its track, and the old release
        private void AlbumShouldBeAsBefore(TestAlbum album)
        {
            album.OldPaths.Select(File.ReadAllText).Should().Equal(album.OldPaths.Select((p, i) => "old " + (i + 1)));

            var rows = Db.All<TrackFile>().Where(f => f.AlbumId == album.Album.Id).ToList();
            rows.Select(f => f.Path).Should().BeEquivalentTo(album.OldPaths);

            var tracks = Db.All<Track>().ToDictionary(t => t.Id);
            for (var i = 0; i < album.OldTracks.Count; i++)
            {
                tracks[album.OldTracks[i].Id].TrackFileId.Should().Be(rows.Single(f => f.Path == album.OldPaths[i]).Id);
            }

            rows.Single(f => f.Path == album.OldPaths[0]).Size.Should().Be(101);
            Db.All<AlbumRelease>().Single(r => r.Id == album.OldRelease.Id).Monitored.Should().BeTrue();
            Db.All<AlbumRelease>().Single(r => r.Id == album.NewRelease.Id).Monitored.Should().BeFalse();
            Directory.Exists(Path.Combine(album.Folder, ".lidarr-replaced")).Should().BeFalse();
        }

        [Test]
        public void should_recycle_the_previous_files_once_a_track_of_the_new_release_imports()
        {
            var album = GivenAlbum("A");
            GivenNewFileLandsAs("01 - Single.flac");

            var results = Import(Incoming(album, album.NewRelease, album.NewTrack));

            ExceptionVerification.ExpectedWarns(0);
            results.Should().ContainSingle(r => r.Result == ImportResultType.Imported);
            Directory.GetFiles(album.Folder, "*", SearchOption.AllDirectories).Select(Path.GetFileName).Should().BeEquivalentTo(new[] { "01 - Single.flac" });
            _recycled.Select(r => (Path.GetFileName(r.Path), r.Subfolder, r.Content)).Should().BeEquivalentTo(new[]
            {
                ("01 - Song.mp3", Path.Combine("Artist", "A"), "old 1"),
                ("02 - Song.mp3", Path.Combine("Artist", "A"), "old 2"),
                ("03 - Song.mp3", Path.Combine("Artist", "A"), "old 3")
            });
            Db.All<TrackFile>().Select(f => Path.GetFileName(f.Path)).Should().BeEquivalentTo(new[] { "01 - Single.flac" });
            Db.All<Track>().Where(t => album.OldTracks.Select(o => o.Id).Contains(t.Id)).Should().OnlyContain(t => t.TrackFileId == 0);
            Db.All<AlbumRelease>().Single(r => r.Id == album.NewRelease.Id).Monitored.Should().BeTrue();
        }

        [Test]
        public void should_put_the_album_back_when_nothing_of_the_new_release_imports()
        {
            var album = GivenAlbum("A");
            GivenNothingImports();

            var results = Import(Incoming(album, album.NewRelease, album.NewTrack));

            ExceptionVerification.ExpectedWarns(2);
            results.Should().OnlyContain(r => r.Result != ImportResultType.Imported);
            AlbumShouldBeAsBefore(album);
            _recycled.Should().BeEmpty();
        }

        [Test]
        public void should_put_the_previous_file_back_over_a_new_file_whose_import_failed_after_its_transfer()
        {
            var album = GivenAlbum("A");
            Mocker.SetConstant<IUpgradeMediaFiles>(Mocker.Resolve<UpgradeMediaFileService>());

            // The new file takes the old file's name, then writing its tags throws
            Mocker.GetMock<IMoveTrackFiles>()
                  .Setup(s => s.CopyTrackFile(It.IsAny<TrackFile>(), It.IsAny<LocalTrack>()))
                  .Returns<TrackFile, LocalTrack>((f, l) =>
                  {
                      f.Path = album.OldPaths[0];
                      File.WriteAllText(f.Path, "new");
                      return f;
                  });
            Mocker.GetMock<IAudioTagService>()
                  .Setup(s => s.WriteTags(It.IsAny<TrackFile>(), It.IsAny<bool>(), It.IsAny<bool>()))
                  .Throws(new InvalidOperationException("Sequence contains no elements"));

            var results = Import(Incoming(album, album.NewRelease, album.NewTrack));

            ExceptionVerification.ExpectedWarns(2);
            results.Should().OnlyContain(r => r.Result != ImportResultType.Imported);
            AlbumShouldBeAsBefore(album);
            _recycled.Should().ContainSingle(r => r.Content == "new");
        }

        [Test]
        public void should_put_every_album_back_when_a_later_one_fails_to_move_aside()
        {
            var first = GivenAlbum("A");
            var second = GivenAlbum("B", "CD2");

            // A plain file has the hidden folder's name, so B's files can't move aside
            File.WriteAllText(Path.Combine(second.Folder, ".lidarr-replaced"), "not a folder");

            Assert.Catch<IOException>(() => Import(Incoming(first, first.NewRelease, first.NewTrack), Incoming(second, second.NewRelease, second.NewTrack)));

            AlbumShouldBeAsBefore(first);
            second.OldPaths.Should().OnlyContain(p => File.Exists(p));
            Db.All<TrackFile>().Where(f => f.AlbumId == second.Album.Id).Select(f => f.Path).Should().BeEquivalentTo(second.OldPaths);
            _recycled.Should().BeEmpty();
        }

        [Test]
        public void should_put_the_album_back_when_switching_its_release_fails()
        {
            var album = GivenAlbum("A");
            var releases = Mocker.Resolve<ReleaseService>();
            var failing = new Mock<IReleaseService>();
            failing.Setup(s => s.SetMonitored(It.Is<AlbumRelease>(r => r.Id == album.NewRelease.Id))).Throws(new InvalidOperationException("database is locked"));
            failing.Setup(s => s.SetMonitored(It.Is<AlbumRelease>(r => r.Id != album.NewRelease.Id))).Returns<AlbumRelease>(r => releases.SetMonitored(r));
            Mocker.SetConstant(failing.Object);

            Assert.Catch<InvalidOperationException>(() => Import(Incoming(album, album.NewRelease, album.NewTrack)));

            AlbumShouldBeAsBefore(album);
        }

        [Test]
        public void should_not_duplicate_the_rows_a_partial_delete_left_when_it_puts_the_album_back()
        {
            var album = GivenAlbum("A");
            var files = Mocker.Resolve<MediaFileService>();
            var secondId = Db.All<TrackFile>().Single(f => f.Path == album.OldPaths[1]).Id;

            // RemoveExistingTrackFiles deletes the first row, then the database refuses the second
            var failing = new Mock<IMediaFileService>();
            failing.Setup(s => s.GetFilesByAlbum(It.IsAny<int>())).Returns<int>(id => files.GetFilesByAlbum(id));
            failing.Setup(s => s.GetFileWithPath(It.IsAny<string>())).Returns<string>(p => files.GetFileWithPath(p));
            failing.Setup(s => s.AddMany(It.IsAny<List<TrackFile>>())).Callback<List<TrackFile>>(l => files.AddMany(l));
            failing.Setup(s => s.Delete(It.IsAny<TrackFile>(), It.IsAny<DeleteMediaFileReason>()))
                   .Callback<TrackFile, DeleteMediaFileReason>((f, r) =>
                   {
                       if (f.Id == secondId)
                       {
                           throw new InvalidOperationException("database is locked");
                       }

                       files.Delete(f, r);
                   });
            Mocker.SetConstant(failing.Object);

            Assert.Catch<InvalidOperationException>(() => Import(Incoming(album, album.NewRelease, album.NewTrack)));

            AlbumShouldBeAsBefore(album);
        }

        [Test]
        public void should_recycle_what_an_interrupted_import_left_aside()
        {
            var album = GivenAlbum("A");
            var hidden = Path.Combine(album.Folder, ".lidarr-replaced");
            Directory.CreateDirectory(hidden);
            File.WriteAllText(Path.Combine(hidden, "01 - Song.mp3"), "left over");
            GivenNewFileLandsAs("01 - Single.flac");

            Import(Incoming(album, album.NewRelease, album.NewTrack));

            _recycled.Select(r => r.Content).Should().BeEquivalentTo(new[] { "left over", "old 1", "old 2", "old 3" });
            _recycled.Should().OnlyContain(r => r.Subfolder == Path.Combine("Artist", "A"));
            Directory.Exists(hidden).Should().BeFalse();
        }

        [Test]
        public void should_record_the_new_files_when_recycling_the_previous_ones_fails()
        {
            var album = GivenAlbum("A");
            GivenNewFileLandsAs("01 - Single.flac");
            Mocker.GetMock<IRecycleBinProvider>()
                  .Setup(s => s.DeleteFile(It.IsAny<string>(), It.IsAny<string>()))
                  .Throws(new RecycleBinException("bin is full"));

            var results = Import(Incoming(album, album.NewRelease, album.NewTrack));

            ExceptionVerification.ExpectedErrors(1);
            results.Should().ContainSingle(r => r.Result == ImportResultType.Imported);
            Db.All<TrackFile>().Select(f => Path.GetFileName(f.Path)).Should().BeEquivalentTo(new[] { "01 - Single.flac" });
            Directory.GetFiles(Path.Combine(album.Folder, ".lidarr-replaced")).Should().HaveCount(3, "they wait there, as the error says");
        }

        [Test]
        public void should_upgrade_the_same_release_through_the_real_upgrade_service()
        {
            var album = GivenAlbum("A");
            Mocker.SetConstant<IUpgradeMediaFiles>(Mocker.Resolve<UpgradeMediaFileService>());
            Mocker.GetMock<IMoveTrackFiles>()
                  .Setup(s => s.CopyTrackFile(It.IsAny<TrackFile>(), It.IsAny<LocalTrack>()))
                  .Returns<TrackFile, LocalTrack>((f, l) =>
                  {
                      f.Path = Path.Combine(album.Folder, "01 - Song.flac");
                      File.WriteAllText(f.Path, "new");
                      return f;
                  });

            // Import specs load the track's file as their own copy
            var track = album.OldTracks[0];
            track.TrackFile = new TrackFile { Id = track.TrackFileId, AlbumId = album.Album.Id, Path = album.OldPaths[0], Quality = new QualityModel(Quality.MP3_320) };

            var results = Import(Incoming(album, album.OldRelease, track));

            // develop warns here too: its copy points at a path the files have already left
            ExceptionVerification.IgnoreWarns();
            results.Should().ContainSingle(r => r.Result == ImportResultType.Imported);
            Directory.GetFiles(album.Folder, "*", SearchOption.AllDirectories).Select(Path.GetFileName).Should().BeEquivalentTo(new[] { "01 - Song.flac" });
            _recycled.Select(r => r.Content).Should().BeEquivalentTo(new[] { "old 1", "old 2", "old 3" });
            Db.All<TrackFile>().Select(f => Path.GetFileName(f.Path)).Should().BeEquivalentTo(new[] { "01 - Song.flac" });
        }
    }
}
