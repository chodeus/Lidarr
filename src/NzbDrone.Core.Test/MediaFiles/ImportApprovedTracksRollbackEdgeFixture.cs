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
    // Setup mirrors ImportApprovedTracksRealServicesRollbackFixture
    [TestFixture]
    public class ImportApprovedTracksRollbackEdgeFixture : DbTest<ImportApprovedTracks, TrackFile>
    {
        private Artist _artist;
        private ArtistMetadata _metadata;
        private TrackService _trackService;
        private List<(string Path, string Subfolder, string Content)> _recycled;
        private List<Action<TrackFileDeletedEvent>> _deletedHandlers;
        private string _unrecyclable;
        private MediaFileService _mediaFileService;

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

            Mocker.GetMock<ILyricFileService>().Setup(s => s.GetFilesByTrackFile(It.IsAny<int>())).Returns(new List<LyricFile>());
            Mocker.GetMock<IMetadataFileService>().Setup(s => s.GetFilesByTrackFile(It.IsAny<int>())).Returns(new List<MetadataFile>());
            Mocker.GetMock<IOtherExtraFileService>().Setup(s => s.GetFilesByTrackFile(It.IsAny<int>())).Returns(new List<OtherExtraFile>());

            Mocker.SetConstant<IMediaFileRepository>(Mocker.Resolve<MediaFileRepository>());
            _mediaFileService = Mocker.Resolve<MediaFileService>();
            Mocker.SetConstant<IMediaFileService>(_mediaFileService);
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
            _unrecyclable = null;
            Mocker.GetMock<IRecycleBinProvider>()
                  .Setup(s => s.DeleteFile(It.IsAny<string>(), It.IsAny<string>()))
                  .Callback<string, string>((path, subfolder) =>
                  {
                      if (path == _unrecyclable)
                      {
                          throw new IOException("Access denied");
                      }

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

        private ImportDecision<LocalTrack> Incoming(TestAlbum album, Track track = null)
        {
            return new ImportDecision<LocalTrack>(new LocalTrack
            {
                Artist = _artist,
                Album = album.Album,
                Release = album.NewRelease,
                Tracks = new List<Track> { track ?? album.NewTrack },
                Path = Path.Combine(TempFolder, "downloads", "Pack [WEB FLAC]", album.Name + " - " + (track ?? album.NewTrack).ForeignTrackId + ".flac"),
                Quality = new QualityModel(Quality.FLAC),
                FileTrackInfo = new ParsedTrackInfo()
            });
        }

        private List<ImportResult> Import(params ImportDecision<LocalTrack>[] decisions)
        {
            // Resolved here, so it gets the services each test sets
            Mocker.SetConstant<IReplaceTrackFiles>(Mocker.Resolve<TrackFileReplacementService>());
            return Subject.Import(decisions.ToList(), true, new DownloadClientItem { DownloadId = "dl-1" });
        }

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

        // Flat naming ({Artist}/{track:00} - {Track Title}): a single and its album both name track 1 the same
        [Test]
        public void should_keep_the_new_file_another_album_imported_at_the_path_it_puts_back()
        {
            var a = GivenAlbum("A", "");
            var b = GivenAlbum("B", "");
            var sharedPath = b.OldPaths[0];

            Mocker.GetMock<IUpgradeMediaFiles>()
                  .Setup(s => s.UpgradeTrackFile(It.IsAny<TrackFile>(), It.IsAny<LocalTrack>(), It.IsAny<bool>()))
                  .Returns<TrackFile, LocalTrack, bool>((f, l, c) =>
                  {
                      if (l.Album.Id != a.Album.Id)
                      {
                          throw new DestinationAlreadyExistsException("destination exists");
                      }

                      f.Path = sharedPath;
                      File.WriteAllText(f.Path, "A new");
                      return new TrackFileMoveResult { TrackFile = f };
                  });

            var results = Import(Incoming(a), Incoming(b));

            results.Should().Contain(r => r.Result == ImportResultType.Imported && r.ImportDecision.Item.Album.Id == a.Album.Id);
            new
            {
                ContentAtSharedPath = File.ReadAllText(sharedPath),
                ARowAtSharedPath = Db.All<TrackFile>().Count(f => f.Path == sharedPath && f.AlbumId == a.Album.Id),
                NewFileRecycled = _recycled.Any(r => r.Content == "A new"),
                BTrackLinked = Db.All<Track>().Single(t => t.Id == b.OldTracks[0].Id).TrackFileId > 0
            }.Should().BeEquivalentTo(new { ContentAtSharedPath = "A new", ARowAtSharedPath = 1, NewFileRecycled = false, BTrackLinked = false });
        }

        // After a crash mid-import, a rescan dropped the rows of the files left aside; the next import of the album recycles nothing
        [Test]
        public void should_recycle_what_a_crashed_import_left_aside_when_the_album_has_no_files_now()
        {
            var album = GivenAlbum("A");
            var hidden = Path.Combine(_artist.Path, ".lidarr-replaced", album.Album.Id.ToString());
            Directory.CreateDirectory(hidden);
            File.WriteAllText(Path.Combine(hidden, "A01 - Song.mp3"), "left over");
            GivenOnlyTheseImport(album);

            var results = Import(Incoming(album));

            results.Should().ContainSingle(r => r.Result == ImportResultType.Imported);
            new
            {
                Recycled = _recycled.Select(r => r.Content).ToArray(),
                HiddenFolderLeft = Directory.Exists(Path.Combine(_artist.Path, ".lidarr-replaced"))
            }.Should().BeEquivalentTo(new { Recycled = new[] { "left over" }, HiddenFolderLeft = false });
        }

        // The real history service runs on TrackFileDeletedEvent, as in the app
        [Test]
        public void should_not_leave_track_file_deleted_history_for_files_it_put_back()
        {
            Mocker.SetConstant<IHistoryRepository>(Mocker.Resolve<EntityHistoryRepository>());
            var history = Mocker.Resolve<EntityHistoryService>();
            _deletedHandlers.Add(e => history.Handle(e));
            Mocker.SetConstant<IHistoryService>(history);

            var album = GivenAlbum("A", "A", "A", "A");
            GivenOnlyTheseImport();

            var results = Import(Incoming(album));

            results.Should().OnlyContain(r => r.Result != ImportResultType.Imported);
            album.OldPaths.Should().OnlyContain(p => File.Exists(p));
            Db.All<TrackFile>().Where(f => f.AlbumId == album.Album.Id).Should().HaveCount(3);
            Db.All<EntityHistory>().Where(h => h.EventType == EntityHistoryEventType.TrackFileDeleted).Select(h => Path.GetFileName(h.SourceTitle))
              .Should().BeEquivalentTo(Array.Empty<string>(), "every file is back on disk and in the library");
        }

        [Test]
        public void should_keep_other_deletion_history_when_it_puts_files_back()
        {
            Mocker.SetConstant<IHistoryRepository>(Mocker.Resolve<EntityHistoryRepository>());
            var history = Mocker.Resolve<EntityHistoryService>();
            _deletedHandlers.Add(e => history.Handle(e));
            Mocker.SetConstant<IHistoryService>(history);

            var album = GivenAlbum("A", "A", "A", "A");
            GivenOnlyTheseImport();
            int Deleted(int trackId, string path, DateTime date, string reason = "Upgrade")
            {
                var entry = new EntityHistory { EventType = EntityHistoryEventType.TrackFileDeleted, Date = date, Quality = new QualityModel(Quality.MP3_320), SourceTitle = path, ArtistId = _artist.Id, AlbumId = album.Album.Id, TrackId = trackId };
                entry.Data.Add("Reason", reason);
                return Db.Insert(entry).Id;
            }

            // Each differs from what this import's removal writes in one way: earlier, another track, another path, another reason
            var kept = new[]
            {
                Deleted(album.OldTracks[0].Id, album.OldPaths[0], DateTime.UtcNow.AddDays(-1)),
                Deleted(album.NewTrack.Id, album.OldPaths[0], DateTime.UtcNow.AddMinutes(5)),
                Deleted(album.OldTracks[0].Id, Path.Combine(_artist.Path, "A", "elsewhere.mp3"), DateTime.UtcNow.AddMinutes(5)),
                Deleted(album.OldTracks[0].Id, album.OldPaths[0], DateTime.UtcNow.AddMinutes(5), "Manual")
            };

            Import(Incoming(album));

            Db.All<EntityHistory>().Select(h => h.Id).Should().BeEquivalentTo(kept);
        }

        [Test]
        public void should_still_restore_the_album_when_removing_its_deletion_history_fails()
        {
            Mocker.GetMock<IHistoryService>()
                  .Setup(s => s.RemoveTrackFileDeleted(It.IsAny<int>(), It.IsAny<Dictionary<int, string>>(), It.IsAny<DateTime>()))
                  .Throws(new InvalidOperationException("database is locked"));
            var album = GivenAlbum("A", "A", "A", "A");
            GivenOnlyTheseImport();

            Import(Incoming(album));

            ExceptionVerification.ExpectedErrors(0);
            ExceptionVerification.ExpectedWarns(3);
            album.OldPaths.Should().OnlyContain(p => File.Exists(p));
            Db.All<TrackFile>().Select(f => f.Path).Should().BeEquivalentTo(album.OldPaths);
        }

        // Error says the switch failed, but the in-memory flags flipped first and the events go out after
        [Test]
        public void should_report_the_release_the_database_kept_when_switching_fails()
        {
            var album = GivenAlbum("A", "A");
            GivenOnlyTheseImport(album);
            var failing = new Mock<IReleaseService>();
            failing.Setup(s => s.SetMonitored(It.IsAny<AlbumRelease>())).Throws(new InvalidOperationException("database is locked"));
            Mocker.SetConstant(failing.Object);
            var imported = new List<TrackImportedEvent>();
            Mocker.GetMock<IEventAggregator>()
                  .Setup(s => s.PublishEvent(It.IsAny<TrackImportedEvent>()))
                  .Callback<TrackImportedEvent>(e => imported.Add(e));

            Import(Incoming(album));

            var kept = Db.All<AlbumRelease>().Single(r => r.AlbumId == album.Album.Id && r.Monitored).Id;
            imported.Should().HaveCount(1);
            imported.Select(e => e.TrackInfo.Album.AlbumReleases.Value.Single(r => r.Monitored).Id).Should().Equal(new[] { kept });
        }

        // Remote candidate (Id 0) whose album a refresh added before the import; develop switched it
        [Test]
        public void should_switch_the_release_when_the_album_was_added_after_identification()
        {
            Mocker.SetConstant<IAlbumRepository>(Mocker.Resolve<AlbumRepository>());
            Mocker.SetConstant<IAlbumService>(Mocker.Resolve<AlbumService>());
            var album = GivenAlbum("A", "A");
            GivenOnlyTheseImport(album);
            var decision = Incoming(album);
            decision.Item.Album = new Album { Id = 0, ForeignAlbumId = album.Album.ForeignAlbumId, Artist = _artist, AlbumReleases = new List<AlbumRelease>() };
            decision.Item.Release = new AlbumRelease { ForeignReleaseId = album.NewRelease.ForeignReleaseId };
            decision.Item.Tracks = new List<Track> { new Track { ForeignTrackId = album.NewTrack.ForeignTrackId } };

            var results = Import(decision);

            results.Should().ContainSingle(r => r.Result == ImportResultType.Imported);
            Db.All<AlbumRelease>().Single(r => r.Id == album.NewRelease.Id).Monitored.Should().BeTrue();
        }

        // The same database read failure the AddMany catch handles, 15 lines earlier
        [Test]
        public void should_put_the_album_back_when_loading_the_quality_profile_fails()
        {
            var album = GivenAlbum("A", "A", "A", "A");
            GivenOnlyTheseImport(album);
            _artist.QualityProfile = new LazyLoaded<QualityProfile>();

            var thrown = Assert.Catch<Exception>(() => Import(Incoming(album)));
            thrown.StackTrace.Should().Contain("QualityModelComparer..ctor");

            new
            {
                FilesOnDisk = album.OldPaths.Count(File.Exists),
                Rows = Db.All<TrackFile>().Count(f => f.AlbumId == album.Album.Id),
                HiddenFolder = Directory.Exists(Path.Combine(_artist.Path, "A", ".lidarr-replaced"))
            }.Should().BeEquivalentTo(new { FilesOnDisk = 3, Rows = 3, HiddenFolder = false });
        }

        [Test]
        public void should_recycle_the_lyrics_of_a_file_whose_path_another_album_took()
        {
            Mocker.SetConstant<IExtraFileRepository<LyricFile>>(Mocker.Resolve<LyricFileRepository>());
            var lyrics = Mocker.Resolve<LyricFileService>();
            Mocker.SetConstant<ILyricFileService>(lyrics);
            _deletedHandlers.Add(e => lyrics.Handle(e));

            var a = GivenAlbum("A", "");
            var b = GivenAlbum("B", "");
            var sharedPath = b.OldPaths[0];
            var bRow = Db.All<TrackFile>().Single(f => f.Path == sharedPath);
            var lrcPath = Path.ChangeExtension(sharedPath, ".lrc");
            File.WriteAllText(lrcPath, "B lyric");
            Db.Insert(new LyricFile { ArtistId = _artist.Id, AlbumId = b.Album.Id, TrackFileId = bRow.Id, RelativePath = Path.GetFileName(lrcPath), Extension = ".lrc", Added = DateTime.UtcNow, LastUpdated = DateTime.UtcNow });

            Mocker.GetMock<IUpgradeMediaFiles>()
                  .Setup(s => s.UpgradeTrackFile(It.IsAny<TrackFile>(), It.IsAny<LocalTrack>(), It.IsAny<bool>()))
                  .Returns<TrackFile, LocalTrack, bool>((f, l, c) =>
                  {
                      if (l.Album.Id != a.Album.Id)
                      {
                          throw new DestinationAlreadyExistsException("destination exists");
                      }

                      f.Path = sharedPath;
                      File.WriteAllText(f.Path, "A new");
                      return new TrackFileMoveResult { TrackFile = f };
                  });

            Import(Incoming(a), Incoming(b));

            // B's file went to the bin, as develop's upgrade would have sent it, so its lyric goes too
            new
            {
                LyricRecycled = _recycled.Any(r => r.Content == "B lyric"),
                LyricOnDisk = File.Exists(lrcPath),
                LyricRows = Db.All<LyricFile>().Count
            }.Should().BeEquivalentTo(new { LyricRecycled = true, LyricOnDisk = false, LyricRows = 0 });
        }

        [Test]
        public void should_put_back_the_other_files_when_one_cannot_go_back()
        {
            var album = GivenAlbum("A", "A", "A", "A");
            _unrecyclable = album.OldPaths[1];

            Mocker.GetMock<IUpgradeMediaFiles>()
                  .Setup(s => s.UpgradeTrackFile(It.IsAny<TrackFile>(), It.IsAny<LocalTrack>(), It.IsAny<bool>()))
                  .Returns<TrackFile, LocalTrack, bool>((f, l, c) =>
                  {
                      // The transfer landed, then the import failed
                      File.WriteAllText(album.OldPaths[1], "new");
                      throw new DestinationAlreadyExistsException("destination exists");
                  });

            var results = Import(Incoming(album));

            ExceptionVerification.ExpectedErrors(1);
            results.Should().OnlyContain(r => r.Result != ImportResultType.Imported);
            new
            {
                Back = new[] { album.OldPaths[0], album.OldPaths[2] }.Select(File.ReadAllText).ToArray(),
                Rows = Db.All<TrackFile>().Where(f => f.AlbumId == album.Album.Id).Select(f => f.Path).ToArray(),
                Linked = Db.All<Track>().Where(t => t.TrackFileId > 0).Select(t => t.Id).ToArray(),
                Waiting = Directory.GetFiles(Path.Combine(_artist.Path, "A", ".lidarr-replaced", album.Album.Id.ToString())).Select(File.ReadAllText).ToArray()
            }.Should().BeEquivalentTo(new
            {
                Back = new[] { "A old 1", "A old 3" },
                Rows = new[] { album.OldPaths[0], album.OldPaths[2] },
                Linked = new[] { album.OldTracks[0].Id, album.OldTracks[2].Id },
                Waiting = new[] { "A old 2" }
            });
        }

        [Test]
        public void should_put_the_old_file_back_when_linking_the_new_files_fails()
        {
            var album = GivenAlbum("A", "A");
            var path = album.OldPaths[0];

            Mocker.GetMock<IUpgradeMediaFiles>()
                  .Setup(s => s.UpgradeTrackFile(It.IsAny<TrackFile>(), It.IsAny<LocalTrack>(), It.IsAny<bool>()))
                  .Returns<TrackFile, LocalTrack, bool>((f, l, c) =>
                  {
                      // Same naming and extension, e.g. a FLAC 24-bit upgrade of a FLAC album
                      f.Path = path;
                      File.WriteAllText(f.Path, "new");
                      return new TrackFileMoveResult { TrackFile = f };
                  });

            var failOnce = true;
            var tracks = new Mock<ITrackService>();
            tracks.Setup(s => s.GetTracksByFileId(It.IsAny<IEnumerable<int>>())).Returns<IEnumerable<int>>(ids => _trackService.GetTracksByFileId(ids));
            tracks.Setup(s => s.GetTracksByReleases(It.IsAny<List<int>>())).Returns<List<int>>(ids => _trackService.GetTracksByReleases(ids));
            tracks.Setup(s => s.SetFileIds(It.IsAny<List<Track>>())).Callback<List<Track>>(t =>
            {
                if (failOnce)
                {
                    failOnce = false;
                    throw new InvalidOperationException("database is locked");
                }

                _trackService.SetFileIds(t);
            });
            Mocker.SetConstant(tracks.Object);

            Assert.Catch<InvalidOperationException>(() => Import(Incoming(album)));

            new
            {
                Content = File.ReadAllText(path),
                OldRecycled = _recycled.Any(r => r.Content == "A old 1"),
                RowQuality = Db.All<TrackFile>().Single(f => f.Path == path).Quality.Quality,
                OldTrackLinked = Db.All<Track>().Single(t => t.Id == album.OldTracks[0].Id).TrackFileId > 0
            }.Should().BeEquivalentTo(new { Content = "A old 1", OldRecycled = false, RowQuality = Quality.MP3_320, OldTrackLinked = true });
        }

        [Test]
        public void should_still_put_the_album_back_when_removing_the_unlinked_new_rows_fails()
        {
            var album = GivenAlbum("A", "A", "A");
            var newPath = Path.Combine(_artist.Path, "A", "new.flac");

            Mocker.GetMock<IUpgradeMediaFiles>()
                  .Setup(s => s.UpgradeTrackFile(It.IsAny<TrackFile>(), It.IsAny<LocalTrack>(), It.IsAny<bool>()))
                  .Returns<TrackFile, LocalTrack, bool>((f, l, c) =>
                  {
                      f.Path = newPath;
                      File.WriteAllText(f.Path, "new");
                      return new TrackFileMoveResult { TrackFile = f };
                  });

            var tracks = new Mock<ITrackService>();
            tracks.Setup(s => s.GetTracksByFileId(It.IsAny<IEnumerable<int>>())).Returns<IEnumerable<int>>(ids => _trackService.GetTracksByFileId(ids));
            tracks.Setup(s => s.GetTracksByReleases(It.IsAny<List<int>>())).Returns<List<int>>(ids => _trackService.GetTracksByReleases(ids));
            tracks.Setup(s => s.SetFileIds(It.Is<List<Track>>(t => t.Any(x => x.Id == album.NewTrack.Id)))).Throws(new InvalidOperationException("database is locked"));
            tracks.Setup(s => s.SetFileIds(It.Is<List<Track>>(t => t.All(x => x.Id != album.NewTrack.Id)))).Callback<List<Track>>(t => _trackService.SetFileIds(t));
            Mocker.SetConstant(tracks.Object);

            var files = new Mock<IMediaFileService>();
            files.Setup(s => s.GetFilesByAlbum(It.IsAny<int>())).Returns<int>(id => _mediaFileService.GetFilesByAlbum(id));
            files.Setup(s => s.Delete(It.IsAny<TrackFile>(), It.IsAny<DeleteMediaFileReason>())).Callback<TrackFile, DeleteMediaFileReason>((f, r) => _mediaFileService.Delete(f, r));
            files.Setup(s => s.AddMany(It.IsAny<List<TrackFile>>())).Callback<List<TrackFile>>(f => _mediaFileService.AddMany(f));
            files.Setup(s => s.GetFileWithPath(It.IsAny<string>())).Returns<string>(p => _mediaFileService.GetFileWithPath(p));
            files.Setup(s => s.DeleteMany(It.IsAny<List<TrackFile>>(), It.IsAny<DeleteMediaFileReason>())).Throws(new InvalidOperationException("database is locked"));
            Mocker.SetConstant(files.Object);

            Assert.Catch<InvalidOperationException>(() => Import(Incoming(album)));

            ExceptionVerification.ExpectedWarns(1);
            album.OldPaths.Select(File.ReadAllText).Should().BeEquivalentTo(new[] { "A old 1", "A old 2" });
            Db.All<Track>().Where(t => t.TrackFileId > 0).Select(t => t.Id).Should().BeEquivalentTo(album.OldTracks.Select(t => t.Id));
        }

        [Test]
        public void should_keep_a_lyric_waiting_with_the_track_file_that_cannot_go_back()
        {
            Mocker.SetConstant<IExtraFileRepository<LyricFile>>(Mocker.Resolve<LyricFileRepository>());
            var lyrics = Mocker.Resolve<LyricFileService>();
            Mocker.SetConstant<ILyricFileService>(lyrics);
            _deletedHandlers.Add(e => lyrics.Handle(e));

            var album = GivenAlbum("A", "A", "A", "A");
            var row = Db.All<TrackFile>().Single(f => f.Path == album.OldPaths[1]);
            var lrcPath = Path.ChangeExtension(album.OldPaths[1], ".lrc");
            File.WriteAllText(lrcPath, "A lyric 2");
            Db.Insert(new LyricFile { ArtistId = _artist.Id, AlbumId = album.Album.Id, TrackFileId = row.Id, RelativePath = Path.Combine("A", Path.GetFileName(lrcPath)), Extension = ".lrc", Added = DateTime.UtcNow, LastUpdated = DateTime.UtcNow });
            _unrecyclable = album.OldPaths[1];

            Mocker.GetMock<IUpgradeMediaFiles>()
                  .Setup(s => s.UpgradeTrackFile(It.IsAny<TrackFile>(), It.IsAny<LocalTrack>(), It.IsAny<bool>()))
                  .Returns<TrackFile, LocalTrack, bool>((f, l, c) =>
                  {
                      File.WriteAllText(album.OldPaths[1], "new");
                      throw new DestinationAlreadyExistsException("destination exists");
                  });

            Import(Incoming(album));

            var waiting = Path.Combine(_artist.Path, "A", ".lidarr-replaced", album.Album.Id.ToString());
            new
            {
                TrackFileWaits = File.Exists(Path.Combine(waiting, Path.GetFileName(album.OldPaths[1]))),
                LyricWaits = File.Exists(Path.Combine(waiting, Path.GetFileName(lrcPath))),
                LyricOnDiskWithoutRow = File.Exists(lrcPath) && !Db.All<LyricFile>().Any()
            }.Should().BeEquivalentTo(new { TrackFileWaits = true, LyricWaits = true, LyricOnDiskWithoutRow = false });
        }

        [Test]
        public void should_put_back_the_other_files_when_looking_one_up_fails()
        {
            var album = GivenAlbum("A", "A", "A", "A");
            GivenOnlyTheseImport();

            var files = new Mock<IMediaFileService>();
            files.Setup(s => s.GetFilesByAlbum(It.IsAny<int>())).Returns<int>(id => _mediaFileService.GetFilesByAlbum(id));
            files.Setup(s => s.Delete(It.IsAny<TrackFile>(), It.IsAny<DeleteMediaFileReason>())).Callback<TrackFile, DeleteMediaFileReason>((f, r) => _mediaFileService.Delete(f, r));
            files.Setup(s => s.AddMany(It.IsAny<List<TrackFile>>())).Callback<List<TrackFile>>(f => _mediaFileService.AddMany(f));
            files.Setup(s => s.GetFilesWithBasePath(It.IsAny<string>())).Returns<string>(p => _mediaFileService.GetFilesWithBasePath(p));
            files.Setup(s => s.GetFileWithPath(It.IsAny<string>())).Returns<string>(p =>
                p == album.OldPaths[1] ? throw new InvalidOperationException("database is locked") : _mediaFileService.GetFileWithPath(p));
            Mocker.SetConstant(files.Object);

            Import(Incoming(album));

            new
            {
                Back = new[] { album.OldPaths[0], album.OldPaths[2] }.Count(File.Exists),
                Rows = Db.All<TrackFile>().Count(f => f.Path == album.OldPaths[0] || f.Path == album.OldPaths[2])
            }.Should().BeEquivalentTo(new { Back = 2, Rows = 2 });
        }

        [Test]
        public void should_put_the_first_album_back_when_looking_up_a_later_album_fails()
        {
            var first = GivenAlbum("A", "A", "A");
            GivenOnlyTheseImport();
            Mocker.GetMock<IAlbumService>().Setup(s => s.FindById(It.IsAny<string>())).Throws(new InvalidOperationException("database is locked"));

            var remote = new ImportDecision<LocalTrack>(new LocalTrack
            {
                Artist = _artist,
                Album = new Album { Id = 0, ForeignAlbumId = "remote-album", Artist = _artist, AlbumReleases = new List<AlbumRelease>() },
                Release = new AlbumRelease { ForeignReleaseId = "remote-release" },
                Tracks = new List<Track> { new Track { ForeignTrackId = "remote-track" } },
                Path = Path.Combine(TempFolder, "downloads", "Pack [WEB FLAC]", "remote.flac"),
                Quality = new QualityModel(Quality.FLAC),
                FileTrackInfo = new ParsedTrackInfo()
            });

            Assert.Catch<InvalidOperationException>(() => Import(Incoming(first), remote));

            new
            {
                FilesOnDisk = first.OldPaths.Count(File.Exists),
                Rows = Db.All<TrackFile>().Count(f => f.AlbumId == first.Album.Id)
            }.Should().BeEquivalentTo(new { FilesOnDisk = 2, Rows = 2 });
        }

        [Test]
        public void should_restore_the_rows_when_removing_the_empty_backup_folder_fails()
        {
            if (OperatingSystem.IsWindows())
            {
                Assert.Ignore("needs Unix file modes");
                return;
            }

            var album = GivenAlbum("A", "A", "A", "A");
            var hidden = Path.Combine(_artist.Path, "A", ".lidarr-replaced");

            Mocker.GetMock<IUpgradeMediaFiles>()
                  .Setup(s => s.UpgradeTrackFile(It.IsAny<TrackFile>(), It.IsAny<LocalTrack>(), It.IsAny<bool>()))
                  .Returns<TrackFile, LocalTrack, bool>((f, l, c) =>
                  {
                      // The album's own folder inside stays writable, so its files can still leave it
                      File.SetUnixFileMode(hidden, UnixFileMode.UserRead | UnixFileMode.UserExecute);
                      throw new DestinationAlreadyExistsException("destination exists");
                  });

            try
            {
                Import(Incoming(album));
            }
            finally
            {
                File.SetUnixFileMode(hidden, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            ExceptionVerification.ExpectedErrors(0);
            ExceptionVerification.ExpectedWarns(3);
            album.OldPaths.Should().OnlyContain(p => File.Exists(p));
            Db.All<TrackFile>().Select(f => f.Path).Should().BeEquivalentTo(album.OldPaths);
        }

        [Test]
        public void should_put_the_old_file_back_when_linking_and_removing_the_new_rows_both_fail()
        {
            var album = GivenAlbum("A", "A");
            var path = album.OldPaths[0];

            Mocker.GetMock<IUpgradeMediaFiles>()
                  .Setup(s => s.UpgradeTrackFile(It.IsAny<TrackFile>(), It.IsAny<LocalTrack>(), It.IsAny<bool>()))
                  .Returns<TrackFile, LocalTrack, bool>((f, l, c) =>
                  {
                      f.Path = path;
                      File.WriteAllText(f.Path, "new");
                      return new TrackFileMoveResult { TrackFile = f };
                  });

            var failOnce = true;
            var tracks = new Mock<ITrackService>();
            tracks.Setup(s => s.GetTracksByFileId(It.IsAny<IEnumerable<int>>())).Returns<IEnumerable<int>>(ids => _trackService.GetTracksByFileId(ids));
            tracks.Setup(s => s.GetTracksByReleases(It.IsAny<List<int>>())).Returns<List<int>>(ids => _trackService.GetTracksByReleases(ids));
            tracks.Setup(s => s.SetFileIds(It.IsAny<List<Track>>())).Callback<List<Track>>(t =>
            {
                if (failOnce)
                {
                    failOnce = false;
                    throw new InvalidOperationException("database is locked");
                }

                _trackService.SetFileIds(t);
            });
            Mocker.SetConstant(tracks.Object);

            var files = new Mock<IMediaFileService>();
            files.Setup(s => s.GetFilesByAlbum(It.IsAny<int>())).Returns<int>(id => _mediaFileService.GetFilesByAlbum(id));
            files.Setup(s => s.Delete(It.IsAny<TrackFile>(), It.IsAny<DeleteMediaFileReason>())).Callback<TrackFile, DeleteMediaFileReason>((f, r) => _mediaFileService.Delete(f, r));
            files.Setup(s => s.AddMany(It.IsAny<List<TrackFile>>())).Callback<List<TrackFile>>(f => _mediaFileService.AddMany(f));
            files.Setup(s => s.GetFileWithPath(It.IsAny<string>())).Returns<string>(p => _mediaFileService.GetFileWithPath(p));
            files.Setup(s => s.DeleteMany(It.IsAny<List<TrackFile>>(), It.IsAny<DeleteMediaFileReason>())).Throws(new InvalidOperationException("database is locked"));
            Mocker.SetConstant(files.Object);

            Assert.Catch<InvalidOperationException>(() => Import(Incoming(album)));

            new
            {
                Content = File.ReadAllText(path),
                OldRecycled = _recycled.Any(r => r.Content == "A old 1")
            }.Should().BeEquivalentTo(new { Content = "A old 1", OldRecycled = false });
        }

        [Test]
        public void should_put_back_what_moved_aside_when_a_later_file_of_the_album_cannot()
        {
            var album = GivenAlbum("A", "CD1", "CD2");

            // A plain file has the hidden folder's name, so the second file can't move aside
            File.WriteAllText(Path.Combine(_artist.Path, "CD2", ".lidarr-replaced"), "not a folder");
            GivenOnlyTheseImport(album);

            Assert.Catch<IOException>(() => Import(Incoming(album)));

            new
            {
                FilesOnDisk = album.OldPaths.Count(File.Exists),
                LeftAside = Directory.Exists(Path.Combine(_artist.Path, "CD1", ".lidarr-replaced"))
            }.Should().BeEquivalentTo(new { FilesOnDisk = 2, LeftAside = false });
        }

        [Test]
        public void should_put_the_next_album_back_when_putting_one_back_fails()
        {
            var a = GivenAlbum("A", "A");
            var b = GivenAlbum("B", "B");
            GivenOnlyTheseImport();

            var files = new Mock<IMediaFileService>();
            files.Setup(s => s.GetFilesByAlbum(It.IsAny<int>())).Returns<int>(id => _mediaFileService.GetFilesByAlbum(id));
            files.Setup(s => s.Delete(It.IsAny<TrackFile>(), It.IsAny<DeleteMediaFileReason>())).Callback<TrackFile, DeleteMediaFileReason>((f, r) => _mediaFileService.Delete(f, r));
            files.Setup(s => s.GetFileWithPath(It.IsAny<string>())).Returns<string>(p => _mediaFileService.GetFileWithPath(p));
            files.Setup(s => s.AddMany(It.Is<List<TrackFile>>(l => l.All(f => f.AlbumId != a.Album.Id)))).Callback<List<TrackFile>>(f => _mediaFileService.AddMany(f));
            files.Setup(s => s.AddMany(It.Is<List<TrackFile>>(l => l.Any(f => f.AlbumId == a.Album.Id)))).Throws(new InvalidOperationException("database is locked"));
            Mocker.SetConstant(files.Object);

            Import(Incoming(a), Incoming(b));

            ExceptionVerification.ExpectedErrors(1);
            new
            {
                AOnDisk = a.OldPaths.Count(File.Exists),
                BOnDisk = b.OldPaths.Count(File.Exists),
                BRows = Db.All<TrackFile>().Count(f => f.AlbumId == b.Album.Id),
                BTrackLinked = Db.All<Track>().Single(t => t.Id == b.OldTracks[0].Id).TrackFileId > 0
            }.Should().BeEquivalentTo(new { AOnDisk = 1, BOnDisk = 1, BRows = 1, BTrackLinked = true });
        }
    }
}
