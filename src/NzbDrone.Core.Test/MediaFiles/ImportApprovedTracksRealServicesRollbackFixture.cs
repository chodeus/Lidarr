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
using NzbDrone.Core.Extras.Files;
using NzbDrone.Core.Extras.Lyrics;
using NzbDrone.Core.Extras.Metadata.Files;
using NzbDrone.Core.Extras.Others;
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
    // Audit repros: setup mirrors ImportApprovedTracksRollbackFixture
    [TestFixture]
    public class ImportApprovedTracksRealServicesRollbackFixture : DbTest<ImportApprovedTracks, TrackFile>
    {
        private Artist _artist;
        private ArtistMetadata _metadata;
        private TrackService _trackService;
        private List<(string Path, string Subfolder, string Content)> _recycled;
        private List<Action<TrackFileDeletedEvent>> _deletedHandlers;

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
            public string Name { get; set; }
            public Album Album { get; set; }
            public AlbumRelease OldRelease { get; set; }
            public AlbumRelease NewRelease { get; set; }
            public List<string> OldPaths { get; set; }
            public List<Track> OldTracks { get; set; }
            public Track NewTrack { get; set; }
        }

        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<IDiskProvider>(new RealDiskProvider());

            // Extra files: none unless a test adds them
            Mocker.GetMock<ILyricFileService>().Setup(s => s.GetFilesByTrackFile(It.IsAny<int>())).Returns(new List<LyricFile>());
            Mocker.GetMock<IMetadataFileService>().Setup(s => s.GetFilesByTrackFile(It.IsAny<int>())).Returns(new List<MetadataFile>());
            Mocker.GetMock<IOtherExtraFileService>().Setup(s => s.GetFilesByTrackFile(It.IsAny<int>())).Returns(new List<OtherExtraFile>());

            Mocker.SetConstant<IMediaFileRepository>(Mocker.Resolve<MediaFileRepository>());
            Mocker.SetConstant<IMediaFileService>(Mocker.Resolve<MediaFileService>());
            Mocker.SetConstant<IReleaseRepository>(Mocker.Resolve<ReleaseRepository>());
            Mocker.SetConstant<IReleaseService>(Mocker.Resolve<ReleaseService>());
            Mocker.SetConstant<ITrackRepository>(Mocker.Resolve<TrackRepository>());

            _trackService = Mocker.Resolve<TrackService>();
            Mocker.SetConstant<ITrackService>(_trackService);

            _deletedHandlers = new List<Action<TrackFileDeletedEvent>> { e => _trackService.Handle(e) };
            Mocker.GetMock<IEventAggregator>()
                  .Setup(s => s.PublishEvent(It.IsAny<TrackFileDeletedEvent>()))
                  .Callback<TrackFileDeletedEvent>(e => _deletedHandlers.ForEach(h => h(e)));

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

        // folders: relative to the artist folder, one per file ("" = the artist folder itself)
        private TestAlbum GivenAlbum(string name, params string[] folders)
        {
            var album = Builder<Album>.CreateNew().With(a => a.Id = 0).With(a => a.ArtistMetadataId = _metadata.Id).With(a => a.ForeignAlbumId = "album-" + name).Build();
            Db.Insert(album);

            var oldRelease = Builder<AlbumRelease>.CreateNew().With(r => r.Id = 0).With(r => r.AlbumId = album.Id).With(r => r.Monitored = true).With(r => r.ForeignReleaseId = name + "-old").Build();
            var newRelease = Builder<AlbumRelease>.CreateNew().With(r => r.Id = 0).With(r => r.AlbumId = album.Id).With(r => r.Monitored = false).With(r => r.ForeignReleaseId = name + "-new").Build();
            Db.Insert(oldRelease);
            Db.Insert(newRelease);
            album.AlbumReleases = new List<AlbumRelease> { oldRelease, newRelease };
            album.Artist = _artist;

            var test = new TestAlbum { Name = name, Album = album, OldRelease = oldRelease, NewRelease = newRelease, OldPaths = new List<string>(), OldTracks = new List<Track>() };

            for (var i = 0; i < folders.Length; i++)
            {
                var folder = Path.Combine(_artist.Path, folders[i]);
                Directory.CreateDirectory(folder);

                var file = new TrackFile { AlbumId = album.Id, Path = Path.Combine(folder, $"{name}0{i + 1} - Song.mp3"), Quality = new QualityModel(Quality.MP3_320), Size = 100 + i + 1 };
                File.WriteAllText(file.Path, $"{name} old {i + 1}");
                Db.Insert(file);

                var track = Builder<Track>.CreateNew().With(t => t.Id = 0).With(t => t.ForeignTrackId = $"{name}-old-{i + 1}").With(t => t.AlbumReleaseId = oldRelease.Id).With(t => t.TrackFileId = file.Id).Build();
                Db.Insert(track);
                test.OldPaths.Add(file.Path);
                test.OldTracks.Add(track);
            }

            test.NewTrack = Builder<Track>.CreateNew().With(t => t.Id = 0).With(t => t.ForeignTrackId = name + "-new-1").With(t => t.AlbumReleaseId = newRelease.Id).With(t => t.TrackFileId = 0).Build();
            Db.Insert(test.NewTrack);

            return test;
        }

        private ImportDecision<LocalTrack> Incoming(TestAlbum album)
        {
            return new ImportDecision<LocalTrack>(new LocalTrack
            {
                Artist = _artist,
                Album = album.Album,
                Release = album.NewRelease,
                Tracks = new List<Track> { album.NewTrack },
                Path = Path.Combine(TempFolder, "downloads", "Pack [WEB FLAC]", album.Name + " - Single.flac"),
                Quality = new QualityModel(Quality.FLAC),
                FileTrackInfo = new ParsedTrackInfo()
            });
        }

        private List<ImportResult> Import(params TestAlbum[] albums)
        {
            return Subject.Import(albums.Select(Incoming).ToList(), true, new DownloadClientItem { DownloadId = "dl-1" });
        }

        // Albums named in importing land their new file in the artist folder; the rest fail their transfer
        private void GivenOnlyTheseImport(params TestAlbum[] importing)
        {
            var ids = importing.Select(a => a.Album.Id).ToList();
            Mocker.GetMock<IUpgradeMediaFiles>()
                  .Setup(s => s.UpgradeTrackFile(It.IsAny<TrackFile>(), It.IsAny<LocalTrack>(), It.IsAny<bool>()))
                  .Returns<TrackFile, LocalTrack, bool>((f, l, c) =>
                  {
                      if (!ids.Contains(l.Album.Id))
                      {
                          throw new DestinationAlreadyExistsException("destination exists");
                      }

                      Directory.CreateDirectory(_artist.Path);
                      f.Path = Path.Combine(_artist.Path, Path.GetFileName(l.Path));
                      File.WriteAllText(f.Path, "new");
                      return new TrackFileMoveResult { TrackFile = f };
                  });
        }

        private void AlbumShouldBeAsBefore(TestAlbum album)
        {
            album.OldPaths.Where(p => !File.Exists(p)).Should().BeEmpty("every previous file of {0} should be back on disk", album.Name);
            album.OldPaths.Select(File.ReadAllText).Should().Equal(album.OldPaths.Select((p, i) => $"{album.Name} old {i + 1}"));

            var rows = Db.All<TrackFile>().Where(f => f.AlbumId == album.Album.Id).ToList();
            rows.Select(f => f.Path).Should().BeEquivalentTo(album.OldPaths);

            var tracks = Db.All<Track>().ToDictionary(t => t.Id);
            for (var i = 0; i < album.OldTracks.Count; i++)
            {
                tracks[album.OldTracks[i].Id].TrackFileId.Should().Be(rows.Single(f => f.Path == album.OldPaths[i]).Id);
            }

            Db.All<AlbumRelease>().Single(r => r.Id == album.OldRelease.Id).Monitored.Should().BeTrue();
            Db.All<AlbumRelease>().Single(r => r.Id == album.NewRelease.Id).Monitored.Should().BeFalse();
        }

        // P1: two albums share one folder; the one that imports recycles the other's backups
        [Test]
        public void shared_folder_should_keep_the_files_of_the_album_that_imported_nothing_when_it_comes_second()
        {
            var a = GivenAlbum("A", "", "", "");
            var b = GivenAlbum("B", "", "", "");
            GivenOnlyTheseImport(a);

            var results = Import(a, b);

            results.Should().Contain(r => r.Result == ImportResultType.Imported && r.ImportDecision.Item.Album.Id == a.Album.Id);
            StateOf(b).Should().BeEquivalentTo(new { RecycledFiles = Array.Empty<string>(), FilesOnDisk = 3, Rows = 3, RowsWithoutFile = 0, OldReleaseMonitored = true });
            AlbumShouldBeAsBefore(b);
        }

        private object StateOf(TestAlbum album)
        {
            var rows = Db.All<TrackFile>().Where(f => f.AlbumId == album.Album.Id).ToList();
            return new
            {
                RecycledFiles = _recycled.Where(r => Path.GetFileName(r.Path).StartsWith(album.Name)).Select(r => Path.GetFileName(r.Path)).OrderBy(n => n).ToArray(),
                FilesOnDisk = album.OldPaths.Count(File.Exists),
                Rows = rows.Count,
                RowsWithoutFile = rows.Count(f => !File.Exists(f.Path)),
                OldReleaseMonitored = Db.All<AlbumRelease>().Single(r => r.Id == album.OldRelease.Id).Monitored
            };
        }

        [Test]
        public void shared_folder_should_keep_the_files_of_the_album_that_imported_nothing_when_it_comes_first()
        {
            var a = GivenAlbum("A", "", "", "");
            var b = GivenAlbum("B", "", "", "");
            GivenOnlyTheseImport(a);

            var results = Import(b, a);

            results.Should().Contain(r => r.Result == ImportResultType.Imported && r.ImportDecision.Item.Album.Id == a.Album.Id);
            _recycled.Where(r => r.Content.StartsWith("B")).Select(r => r.Content).Should().BeEmpty("B imported nothing, so its files should not be recycled");
            AlbumShouldBeAsBefore(b);
            _recycled.Select(r => r.Content).Should().BeEquivalentTo(new[] { "A old 1", "A old 2", "A old 3" });
        }

        // P2: the real lyric file service runs on TrackFileDeletedEvent, as in the app
        [Test]
        public void should_keep_the_lyrics_of_an_album_it_puts_back()
        {
            Mocker.SetConstant<IExtraFileRepository<LyricFile>>(Mocker.Resolve<LyricFileRepository>());
            var lyrics = Mocker.Resolve<LyricFileService>();
            Mocker.SetConstant<ILyricFileService>(lyrics);
            _deletedHandlers.Add(e => lyrics.Handle(e));

            var album = GivenAlbum("A", "A", "A", "A");
            var firstRow = Db.All<TrackFile>().Single(f => f.Path == album.OldPaths[0]);
            var lrcPath = Path.Combine(_artist.Path, "A", "A01 - Song.lrc");
            File.WriteAllText(lrcPath, "[00:01.00] lyric");
            Db.Insert(new LyricFile { ArtistId = _artist.Id, AlbumId = album.Album.Id, TrackFileId = firstRow.Id, RelativePath = Path.Combine("A", "A01 - Song.lrc"), Extension = ".lrc", Added = DateTime.UtcNow, LastUpdated = DateTime.UtcNow });
            GivenOnlyTheseImport();

            var results = Import(album);

            results.Should().OnlyContain(r => r.Result != ImportResultType.Imported);
            AlbumShouldBeAsBefore(album);
            var restoredRow = Db.All<TrackFile>().Single(f => f.Path == album.OldPaths[0]);
            new
            {
                Recycled = _recycled.Select(r => Path.GetFileName(r.Path)).ToArray(),
                LyricOnDisk = File.Exists(lrcPath),
                LyricRowsForRestoredFile = Db.All<LyricFile>().Count(l => l.TrackFileId == restoredRow.Id),
                LyricRows = Db.All<LyricFile>().Count
            }.Should().BeEquivalentTo(new { Recycled = Array.Empty<string>(), LyricOnDisk = true, LyricRowsForRestoredFile = 1, LyricRows = 1 });
        }

        // P2: the backups are recycled before the new rows are written
        [Test]
        public void should_put_the_album_back_when_recording_the_new_files_fails()
        {
            var album = GivenAlbum("A", "A", "A", "A");
            GivenOnlyTheseImport(album);

            var files = Mocker.Resolve<MediaFileService>();
            var failing = new Mock<IMediaFileService>();
            failing.Setup(s => s.GetFilesByAlbum(It.IsAny<int>())).Returns<int>(id => files.GetFilesByAlbum(id));
            failing.Setup(s => s.GetFileWithPath(It.IsAny<string>())).Returns<string>(p => files.GetFileWithPath(p));
            failing.Setup(s => s.Delete(It.IsAny<TrackFile>(), It.IsAny<DeleteMediaFileReason>())).Callback<TrackFile, DeleteMediaFileReason>((f, r) => files.Delete(f, r));
            failing.Setup(s => s.AddMany(It.IsAny<List<TrackFile>>())).Callback<List<TrackFile>>(l =>
            {
                if (l.Any(f => f.Path.EndsWith("Single.flac")))
                {
                    throw new InvalidOperationException("database is locked");
                }

                files.AddMany(l);
            });
            Mocker.SetConstant(failing.Object);

            Assert.Catch<InvalidOperationException>(() => Import(album));

            new
            {
                State = StateOf(album),
                NewReleaseMonitored = Db.All<AlbumRelease>().Single(r => r.Id == album.NewRelease.Id).Monitored,
                HiddenFolder = Directory.Exists(Path.Combine(_artist.Path, "A", ".lidarr-replaced"))
            }.Should().BeEquivalentTo(new
            {
                State = new { RecycledFiles = Array.Empty<string>(), FilesOnDisk = 3, Rows = 3, RowsWithoutFile = 0, OldReleaseMonitored = true },
                NewReleaseMonitored = false,
                HiddenFolder = false
            });
        }

        // P3: a refresh writes the releases it read while the import ran (read during, write after the restore)
        [Test]
        public void should_keep_the_restored_release_when_a_refresh_writes_releases_it_read_during_the_import()
        {
            var album = GivenAlbum("A", "A", "A", "A");
            var releases = Mocker.Resolve<ReleaseRepository>();
            List<AlbumRelease> snapshot = null;
            Mocker.GetMock<IUpgradeMediaFiles>()
                  .Setup(s => s.UpgradeTrackFile(It.IsAny<TrackFile>(), It.IsAny<LocalTrack>(), It.IsAny<bool>()))
                  .Callback(() => snapshot = releases.FindByAlbum(album.Album.Id))
                  .Throws(new DestinationAlreadyExistsException("destination exists"));

            Import(album);
            releases.UpdateMany(snapshot);

            var monitored = Db.All<AlbumRelease>().Single(r => r.AlbumId == album.Album.Id && r.Monitored);
            var linked = Db.All<Track>().Where(t => t.TrackFileId > 0).Select(t => t.AlbumReleaseId).Distinct();
            linked.Should().Equal(new[] { monitored.Id }, "the restored files should be linked to the monitored release");
        }

        // Other order: the refresh read before the release switch and writes during the import
        [Test]
        public void should_keep_the_restored_release_when_a_refresh_writes_releases_it_read_before_the_import()
        {
            var album = GivenAlbum("A", "A", "A", "A");
            var releases = Mocker.Resolve<ReleaseRepository>();
            var snapshot = releases.FindByAlbum(album.Album.Id);
            Mocker.GetMock<IUpgradeMediaFiles>()
                  .Setup(s => s.UpgradeTrackFile(It.IsAny<TrackFile>(), It.IsAny<LocalTrack>(), It.IsAny<bool>()))
                  .Callback(() => releases.UpdateMany(snapshot))
                  .Throws(new DestinationAlreadyExistsException("destination exists"));

            Import(album);

            AlbumShouldBeAsBefore(album);
        }

        // Input shape 0: the first download of an album without files
        [Test]
        public void should_put_the_release_back_when_an_album_without_files_imports_nothing()
        {
            var album = GivenAlbum("A");
            GivenOnlyTheseImport();

            var results = Import(album);

            ExceptionVerification.ExpectedWarns(2);
            results.Should().OnlyContain(r => r.Result != ImportResultType.Imported);
            AlbumShouldBeAsBefore(album);
            Directory.Exists(Path.Combine(_artist.Path, ".lidarr-replaced")).Should().BeFalse();
        }

        [Test]
        public void should_import_an_album_without_files_without_a_backup_folder()
        {
            var album = GivenAlbum("A");
            GivenOnlyTheseImport(album);

            var results = Import(album);

            ExceptionVerification.ExpectedWarns(0);
            results.Should().ContainSingle(r => r.Result == ImportResultType.Imported);
            Directory.GetDirectories(_artist.Path, ".*", SearchOption.AllDirectories).Should().BeEmpty();
            Db.All<AlbumRelease>().Single(r => r.Id == album.NewRelease.Id).Monitored.Should().BeTrue();
        }

        // Input shape: many files over gapped disc folders and the artist folder itself
        [Test]
        public void should_put_back_files_spread_over_gapped_disc_folders()
        {
            var album = GivenAlbum("A", Path.Combine("A", "CD1"), Path.Combine("A", "CD1"), Path.Combine("A", "CD3"), "");
            GivenOnlyTheseImport();

            Import(album);

            AlbumShouldBeAsBefore(album);
            _recycled.Should().BeEmpty();
            Directory.GetDirectories(_artist.Path, ".*", SearchOption.AllDirectories).Should().BeEmpty();
        }

        [Test]
        public void should_recycle_files_spread_over_gapped_disc_folders_into_their_own_subfolders()
        {
            var album = GivenAlbum("A", Path.Combine("A", "CD1"), Path.Combine("A", "CD1"), Path.Combine("A", "CD3"), "");
            GivenOnlyTheseImport(album);

            Import(album);

            _recycled.Select(r => (r.Content, r.Subfolder)).Should().BeEquivalentTo(new[]
            {
                ("A old 1", Path.Combine("Artist", "A", "CD1")),
                ("A old 2", Path.Combine("Artist", "A", "CD1")),
                ("A old 3", Path.Combine("Artist", "A", "CD3")),
                ("A old 4", "Artist")
            });
            Directory.GetDirectories(_artist.Path, ".*", SearchOption.AllDirectories).Should().BeEmpty();
        }

        // Sentinel/shape: two releases flagged monitored; develop's RemoveExistingTrackFiles never read the flag
        [Test]
        public void should_still_import_when_two_releases_are_monitored()
        {
            var album = GivenAlbum("A", "A", "A", "A");
            album.NewRelease.Monitored = true;
            Db.Update(album.NewRelease);
            GivenOnlyTheseImport(album);

            var results = Import(album);

            results.Should().ContainSingle(r => r.Result == ImportResultType.Imported);
        }
    }
}
