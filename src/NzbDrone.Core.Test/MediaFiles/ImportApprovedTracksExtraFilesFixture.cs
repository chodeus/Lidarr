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
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Extras;
using NzbDrone.Core.Extras.Files;
using NzbDrone.Core.Extras.Lyrics;
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
    // The real extra file services and repositories over the test database
    [TestFixture]
    public class ImportApprovedTracksExtraFilesFixture : DbTest<ImportApprovedTracks, TrackFile>
    {
        private Artist _artist;
        private Album _album;
        private AlbumRelease _release;
        private Track _track;
        private string _download;
        private string _albumFolder;
        private List<string> _recycled;

        [SetUp]
        public void Setup()
        {
            var diskProvider = new Mock<DiskProviderBase>(new FileSystem()) { CallBase = true }.Object;
            Mocker.SetConstant<IDiskProvider>(diskProvider);
            Mocker.SetConstant<IDiskTransferService>(new DiskTransferService(diskProvider, TestLogger));

            Mocker.SetConstant<IMediaFileRepository>(Mocker.Resolve<MediaFileRepository>());
            Mocker.SetConstant<IMediaFileService>(Mocker.Resolve<MediaFileService>());
            Mocker.SetConstant<IReleaseRepository>(Mocker.Resolve<ReleaseRepository>());
            Mocker.SetConstant<IReleaseService>(Mocker.Resolve<ReleaseService>());
            Mocker.SetConstant<ITrackRepository>(Mocker.Resolve<TrackRepository>());
            Mocker.SetConstant<ITrackService>(Mocker.Resolve<TrackService>());

            Mocker.GetMock<IConfigService>().SetupGet(c => c.ImportExtraFiles).Returns(true);
            Mocker.GetMock<IConfigService>().SetupGet(c => c.ExtraFileExtensions).Returns("lrc,cue,log");

            _recycled = new List<string>();
            Mocker.GetMock<IRecycleBinProvider>()
                  .Setup(s => s.DeleteFile(It.IsAny<string>(), It.IsAny<string>()))
                  .Callback<string, string>((path, subfolder) =>
                  {
                      _recycled.Add(Path.GetFileName(path));
                      File.Delete(path);
                  });

            Mocker.SetConstant<IExtraFileRepository<LyricFile>>(Mocker.Resolve<LyricFileRepository>());
            Mocker.SetConstant<ILyricFileService>(Mocker.Resolve<LyricFileService>());
            Mocker.SetConstant<IExtraFileRepository<OtherExtraFile>>(Mocker.Resolve<OtherExtraFileRepository>());
            Mocker.SetConstant<IOtherExtraFileService>(Mocker.Resolve<OtherExtraFileService>());

            var trackService = (TrackService)Mocker.Resolve<ITrackService>();
            var lyricFileService = (LyricFileService)Mocker.Resolve<ILyricFileService>();
            var otherExtraFileService = (OtherExtraFileService)Mocker.Resolve<IOtherExtraFileService>();
            var managers = new List<IManageExtraFiles> { Mocker.Resolve<LyricService>(), Mocker.Resolve<OtherExtraService>() };
            var relinkers = new List<IRelinkExtraFiles> { lyricFileService, otherExtraFileService };
            Mocker.SetConstant<IExtraService>(new ExtraService(Mocker.Resolve<IMediaFileService>(), Mocker.Resolve<IAlbumService>(), Mocker.Resolve<ITrackService>(), diskProvider, Mocker.Resolve<IConfigService>(), managers, relinkers, TestLogger));

            // The bus hands deletions to the real extra file services, as the event aggregator does
            Mocker.GetMock<IEventAggregator>()
                  .Setup(s => s.PublishEvent(It.IsAny<TrackFileDeletedEvent>()))
                  .Callback<TrackFileDeletedEvent>(e =>
                  {
                      trackService.Handle(e);
                      lyricFileService.Handle(e);
                      otherExtraFileService.Handle(e);
                  });

            Mocker.GetMock<IHistoryService>()
                  .Setup(s => s.FindByDownloadId(It.IsAny<string>()))
                  .Returns(new List<EntityHistory>());

            var metadata = Builder<ArtistMetadata>.CreateNew().With(a => a.Id = 0).Build();
            Db.Insert(metadata);
            _artist = Builder<Artist>.CreateNew().With(a => a.Id = 0).With(a => a.ArtistMetadataId = metadata.Id).With(a => a.Path = Path.Combine(TempFolder, "Music", "Artist")).Build();
            Db.Insert(_artist);
            _artist.Metadata = metadata;
            _artist.QualityProfile = new QualityProfile { Items = Qualities.QualityFixture.GetDefaultQualities() };

            _album = Builder<Album>.CreateNew().With(a => a.Id = 0).With(a => a.ArtistMetadataId = metadata.Id).Build();
            Db.Insert(_album);
            _release = Builder<AlbumRelease>.CreateNew().With(r => r.Id = 0).With(r => r.AlbumId = _album.Id).With(r => r.Monitored = true).Build();
            Db.Insert(_release);
            _album.AlbumReleases = new List<AlbumRelease> { _release };
            _album.Artist = _artist;
            _track = Builder<Track>.CreateNew().With(t => t.Id = 0).With(t => t.AlbumReleaseId = _release.Id).With(t => t.TrackFileId = 0).Build();
            Db.Insert(_track);

            _download = Path.Combine(TempFolder, "downloads", "Album [FLAC]");
            Directory.CreateDirectory(_download);
            _albumFolder = Path.Combine(_artist.Path, "Album");

            // The new file lands in the album folder under its download name
            Mocker.GetMock<IUpgradeMediaFiles>()
                  .Setup(s => s.UpgradeTrackFile(It.IsAny<TrackFile>(), It.IsAny<LocalTrack>(), It.IsAny<bool>()))
                  .Returns<TrackFile, LocalTrack, bool>((f, l, c) =>
                  {
                      Directory.CreateDirectory(_albumFolder);
                      f.Path = Path.Combine(_albumFolder, Path.GetFileName(l.Path));
                      File.Copy(l.Path, f.Path);
                      return new TrackFileMoveResult { TrackFile = f };
                  });
        }

        private List<ImportResult> Import(string fileName, bool replaceExisting = false, List<ImportDecision<LocalTrack>> others = null, DownloadClientItem item = null)
        {
            var path = Path.Combine(_download, fileName);
            File.WriteAllText(path, "audio");

            var decision = new ImportDecision<LocalTrack>(new LocalTrack
            {
                Artist = _artist,
                Album = _album,
                Release = _release,
                Tracks = new List<Track> { _track },
                Path = path,
                Quality = new QualityModel(Quality.FLAC),
                FileTrackInfo = new ParsedTrackInfo()
            });

            // Resolved here, so it gets the services each test sets
            Mocker.SetConstant<IReplaceTrackFiles>(Mocker.Resolve<TrackFileReplacementService>());
            var decisions = new List<ImportDecision<LocalTrack>> { decision };
            decisions.AddRange(others ?? new List<ImportDecision<LocalTrack>>());

            return Subject.Import(decisions, replaceExisting, item ?? new DownloadClientItem { DownloadId = "dl-1", CanMoveFiles = false });
        }

        // A file of this album that matched no track, such as the image beside the tracks unpackerr split from it
        private ImportDecision<LocalTrack> Rejected(string fileName)
        {
            var path = Path.Combine(_download, fileName);
            File.WriteAllText(path, "audio");

            var localTrack = new LocalTrack
            {
                Artist = _artist,
                Album = _album,
                Release = _release,
                Tracks = new List<Track>(),
                Path = path,
                Quality = new QualityModel(Quality.FLAC),
                FileTrackInfo = new ParsedTrackInfo()
            };

            return new ImportDecision<LocalTrack>(localTrack, new Rejection("Couldn't find similar track"));
        }

        [Test]
        public void should_link_extra_files_imported_with_a_track_to_its_track_file()
        {
            File.WriteAllText(Path.Combine(_download, "01 - Song.lrc"), "[00:01.00] lyric");
            File.WriteAllText(Path.Combine(_download, "01 - Song.log"), "rip log");

            Import("01 - Song.flac").Should().ContainSingle(r => r.Result == ImportResultType.Imported);

            var trackFile = Db.All<TrackFile>().Single();
            new
            {
                Lyrics = Db.All<LyricFile>().Select(l => l.TrackFileId).ToArray(),
                Others = Db.All<OtherExtraFile>().Select(o => o.TrackFileId).ToArray()
            }.Should().BeEquivalentTo(new { Lyrics = new int?[] { trackFile.Id }, Others = new int?[] { trackFile.Id } });
        }

        private void GivenReleaseFileFromAnEarlierImport(string name)
        {
            Directory.CreateDirectory(_albumFolder);
            File.WriteAllText(Path.Combine(_albumFolder, name), "earlier");
            Db.Insert(new OtherExtraFile { ArtistId = _artist.Id, AlbumId = _album.Id, TrackFileId = null, RelativePath = Path.Combine("Album", name), Extension = Path.GetExtension(name), Added = System.DateTime.UtcNow, LastUpdated = System.DateTime.UtcNow });
        }

        [Test]
        public void should_import_release_files_once_into_the_album_folder_under_their_own_names()
        {
            var cue = "FILE \"Album.flac\" WAVE\r\n  TRACK 01 AUDIO\r\n    TITLE \"Caf\u00e9\"\r\n";
            File.WriteAllText(Path.Combine(_download, "Album.cue"), cue, System.Text.Encoding.Unicode);
            File.WriteAllText(Path.Combine(_download, "Album.LOG"), "rip log");
            File.WriteAllText(Path.Combine(_download, "01 - Song.lrc"), "[00:01.00] lyric");

            Import("01 - Song.flac").Should().ContainSingle(r => r.Result == ImportResultType.Imported);

            var trackFile = Db.All<TrackFile>().Single();
            Db.All<OtherExtraFile>().Select(o => (o.RelativePath, o.TrackFileId, o.AlbumId)).Should().BeEquivalentTo(new[]
            {
                (Path.Combine("Album", "Album.cue"), (int?)null, (int?)_album.Id),
                (Path.Combine("Album", "Album.LOG"), (int?)null, (int?)_album.Id)
            });
            File.ReadAllBytes(Path.Combine(_albumFolder, "Album.cue")).Should().Equal(System.Text.Encoding.Unicode.GetPreamble().Concat(System.Text.Encoding.Unicode.GetBytes(cue)));
            Db.All<LyricFile>().Single().TrackFileId.Should().Be(trackFile.Id);
        }

        [Test]
        public void should_not_take_the_lyrics_of_a_track_that_did_not_import_for_a_release_file()
        {
            File.WriteAllText(Path.Combine(_download, "02 - Other.lrc"), "[00:01.00] lyric");
            File.WriteAllText(Path.Combine(_download, "Album.log"), "rip log");

            Import("01 - Song.flac", others: new List<ImportDecision<LocalTrack>> { Rejected("02 - Other.flac") });

            Db.All<OtherExtraFile>().Select(o => Path.GetFileName(o.RelativePath)).Should().Equal("Album.log");
        }

        [Test]
        public void should_recycle_the_previous_release_files_on_a_replacing_import()
        {
            GivenReleaseFileFromAnEarlierImport("Old.log");
            File.WriteAllText(Path.Combine(_download, "Album.log"), "rip log");

            Import("01 - Song.flac", true);

            new
            {
                Recycled = _recycled,
                Rows = Db.All<OtherExtraFile>().Select(o => Path.GetFileName(o.RelativePath)).ToList()
            }.Should().BeEquivalentTo(new { Recycled = new[] { "Old.log" }, Rows = new[] { "Album.log" } });
        }

        [Test]
        public void should_keep_the_previous_release_files_on_an_import_that_does_not_replace()
        {
            GivenReleaseFileFromAnEarlierImport("Old.log");
            File.WriteAllText(Path.Combine(_download, "Album.log"), "rip log");

            Import("01 - Song.flac");

            _recycled.Should().BeEmpty();
            Db.All<OtherExtraFile>().Select(o => Path.GetFileName(o.RelativePath)).Should().BeEquivalentTo("Old.log", "Album.log");
        }

        [Test]
        public void should_not_import_a_release_file_over_a_file_already_there()
        {
            Directory.CreateDirectory(_albumFolder);
            File.WriteAllText(Path.Combine(_albumFolder, "Album.log"), "mine");
            File.WriteAllText(Path.Combine(_download, "Album.log"), "rip log");

            Import("01 - Song.flac", true);

            File.ReadAllText(Path.Combine(_albumFolder, "Album.log")).Should().Be("mine");
            Db.All<OtherExtraFile>().Should().BeEmpty();
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_not_take_release_files_from_a_folder_holding_another_albums_audio()
        {
            File.WriteAllText(Path.Combine(_download, "05 - Another Album Song.flac"), "audio");
            File.WriteAllText(Path.Combine(_download, "Another Album.cue"), "cue");

            Import("01 - Song.flac");

            Db.All<OtherExtraFile>().Should().BeEmpty();
        }

        [Test]
        public void should_import_the_cue_and_log_of_a_split_image()
        {
            File.WriteAllText(Path.Combine(_download, "Artist - Album.cue"), "FILE \"Artist - Album.flac\" WAVE");
            File.WriteAllText(Path.Combine(_download, "Artist - Album.log"), "rip log");

            Import("01 - Song.flac", others: new List<ImportDecision<LocalTrack>> { Rejected("Artist - Album.flac") });

            Db.All<OtherExtraFile>().Select(o => Path.GetFileName(o.RelativePath)).Should().BeEquivalentTo("Artist - Album.cue", "Artist - Album.log");
        }

        [Test]
        public void should_take_no_release_files_beside_a_single_file_download_but_still_replace_the_previous_ones()
        {
            GivenReleaseFileFromAnEarlierImport("Old.log");
            File.WriteAllText(Path.Combine(_download, "Someone Else.log"), "another download's log");

            Import("01 - Song.flac", true, item: new DownloadClientItem { DownloadId = "dl-1", CanMoveFiles = false, OutputPath = new OsPath(Path.Combine(_download, "01 - Song.flac")) });

            new { Recycled = _recycled, Rows = Db.All<OtherExtraFile>().Count }.Should().BeEquivalentTo(new { Recycled = new[] { "Old.log" }, Rows = 0 });
        }

        [Test]
        public void should_recycle_the_previous_release_files_on_a_replacing_import_with_import_extra_files_off()
        {
            Mocker.GetMock<IConfigService>().SetupGet(c => c.ImportExtraFiles).Returns(false);
            GivenReleaseFileFromAnEarlierImport("Old.log");
            File.WriteAllText(Path.Combine(_download, "Album.log"), "rip log");

            Import("01 - Song.flac", true);

            new { Recycled = _recycled, Rows = Db.All<OtherExtraFile>().Count }.Should().BeEquivalentTo(new { Recycled = new[] { "Old.log" }, Rows = 0 });
        }

        [Test]
        public void should_take_a_release_file_back_out_when_its_row_cannot_be_saved()
        {
            var failing = new Mock<IOtherExtraFileService>();
            failing.Setup(s => s.Upsert(It.IsAny<OtherExtraFile>())).Throws(new InvalidOperationException("database is locked"));
            Mocker.SetConstant(failing.Object);
            var otherExtraService = new OtherExtraService(Mocker.Resolve<IConfigService>(), Mocker.Resolve<IDiskProvider>(), Mocker.Resolve<IDiskTransferService>(), failing.Object, Mocker.Resolve<IMediaFileAttributeService>(), TestLogger);
            var managers = new List<IManageExtraFiles> { Mocker.Resolve<LyricService>(), otherExtraService };
            Mocker.SetConstant<IExtraService>(new ExtraService(Mocker.Resolve<IMediaFileService>(), Mocker.Resolve<IAlbumService>(), Mocker.Resolve<ITrackService>(), Mocker.Resolve<IDiskProvider>(), Mocker.Resolve<IConfigService>(), managers, new List<IRelinkExtraFiles>(), TestLogger));
            File.WriteAllText(Path.Combine(_download, "Album.log"), "rip log");

            Import("01 - Song.flac");

            new { InLibrary = File.Exists(Path.Combine(_albumFolder, "Album.log")), InDownload = File.Exists(Path.Combine(_download, "Album.log")) }
                .Should().BeEquivalentTo(new { InLibrary = false, InDownload = true });
            ExceptionVerification.ExpectedWarns(1);
        }

        [TestCase("01 - Song.LRC", 1)]
        [TestCase("01 - Song.backlrc", 0)]
        public void should_match_a_wanted_extension_regardless_of_case_and_only_after_a_dot(string name, int extras)
        {
            File.WriteAllText(Path.Combine(_download, name), "[00:01.00] lyric");

            Import("01 - Song.flac");

            (Db.All<LyricFile>().Count + Db.All<OtherExtraFile>().Count).Should().Be(extras);
        }

        private TrackFile GivenExistingFileWithExtras(string folder)
        {
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, "01 - Song.flac");
            File.WriteAllText(path, "audio");
            var existing = Db.Insert(new TrackFile { AlbumId = _album.Id, Path = path, Size = 5, Modified = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), DateAdded = DateTime.UtcNow, Quality = new QualityModel(Quality.FLAC) });

            File.WriteAllText(Path.Combine(folder, "01 - Song.lrc"), "[00:01.00] lyric");
            File.WriteAllText(Path.Combine(folder, "01 - Song.log"), "rip log");
            Db.Insert(new LyricFile { ArtistId = _artist.Id, AlbumId = _album.Id, TrackFileId = existing.Id, RelativePath = Path.Combine("Album", "01 - Song.lrc"), Extension = ".lrc", Added = DateTime.UtcNow, LastUpdated = DateTime.UtcNow });
            Db.Insert(new OtherExtraFile { ArtistId = _artist.Id, AlbumId = _album.Id, TrackFileId = existing.Id, RelativePath = Path.Combine("Album", "01 - Song.log"), Extension = ".log", Added = DateTime.UtcNow, LastUpdated = DateTime.UtcNow });

            return existing;
        }

        [Test]
        public void should_put_back_the_row_of_a_file_imported_again_when_the_import_fails()
        {
            var folder = Path.Combine(_artist.Path, "Album");
            var existing = GivenExistingFileWithExtras(folder);
            _track.TrackFileId = existing.Id;
            Db.Update(_track);

            // A row whose file is gone holds the path the new track lands on, so AddMany fails
            var other = Builder<Track>.CreateNew().With(t => t.Id = 0).With(t => t.ForeignTrackId = "track-2").With(t => t.AlbumReleaseId = _release.Id).With(t => t.TrackFileId = 0).Build();
            Db.Insert(other);
            Db.Insert(new TrackFile { AlbumId = _album.Id, Path = Path.Combine(folder, "02 - Other.flac"), Size = 5, Modified = DateTime.UtcNow, DateAdded = DateTime.UtcNow, Quality = new QualityModel(Quality.FLAC) });
            File.WriteAllText(Path.Combine(_download, "02 - Other.flac"), "audio");

            var decisions = new List<ImportDecision<LocalTrack>>
            {
                new ImportDecision<LocalTrack>(new LocalTrack { Artist = _artist, Album = _album, Release = _release, Tracks = new List<Track> { _track }, Path = existing.Path, ExistingFile = true, Size = 14, Modified = existing.Modified.AddHours(1), Quality = new QualityModel(Quality.FLAC), FileTrackInfo = new ParsedTrackInfo() }),
                new ImportDecision<LocalTrack>(new LocalTrack { Artist = _artist, Album = _album, Release = _release, Tracks = new List<Track> { other }, Path = Path.Combine(_download, "02 - Other.flac"), Quality = new QualityModel(Quality.FLAC), FileTrackInfo = new ParsedTrackInfo() })
            };

            Mocker.SetConstant<IReplaceTrackFiles>(Mocker.Resolve<TrackFileReplacementService>());
            Assert.Catch(() => Subject.Import(decisions, false));

            var putBack = Db.All<TrackFile>().Single(f => f.Path == existing.Path);
            new
            {
                Track = Db.All<Track>().Single(t => t.Id == _track.Id).TrackFileId,
                Recycled = _recycled,
                Lyrics = Db.All<LyricFile>().Select(l => l.TrackFileId).ToArray(),
                Others = Db.All<OtherExtraFile>().Select(o => o.TrackFileId).ToArray()
            }.Should().BeEquivalentTo(new { Track = putBack.Id, Recycled = new List<string>(), Lyrics = new int?[] { putBack.Id }, Others = new int?[] { putBack.Id } });
        }

        // A rescan imports an existing file again: changed, a tagger rewrote it; switched, a release switch unlinked its track
        [TestCase("changed")]
        [TestCase("switched")]
        public void should_move_the_extras_of_a_file_imported_again_to_its_new_row(string reason)
        {
            var existing = GivenExistingFileWithExtras(Path.Combine(_artist.Path, "Album"));
            var modified = existing.Modified;

            if (reason == "changed")
            {
                _track.TrackFileId = existing.Id;
                Db.Update(_track);
            }

            var decision = new ImportDecision<LocalTrack>(new LocalTrack
            {
                Artist = _artist,
                Album = _album,
                Release = _release,
                Tracks = new List<Track> { _track },
                Path = existing.Path,
                ExistingFile = true,
                Size = reason == "changed" ? 14 : 5,
                Modified = reason == "changed" ? modified.AddHours(1) : modified,
                Quality = new QualityModel(Quality.FLAC),
                FileTrackInfo = new ParsedTrackInfo()
            });

            Mocker.SetConstant<IReplaceTrackFiles>(Mocker.Resolve<TrackFileReplacementService>());
            Subject.Import(new List<ImportDecision<LocalTrack>> { decision }, false);

            var trackFile = Db.All<TrackFile>().Single();
            new
            {
                NewRow = trackFile.Id != existing.Id,
                Recycled = _recycled,
                Lyrics = Db.All<LyricFile>().Select(l => l.TrackFileId).ToArray(),
                Others = Db.All<OtherExtraFile>().Select(o => o.TrackFileId).ToArray()
            }.Should().BeEquivalentTo(new { NewRow = true, Recycled = new List<string>(), Lyrics = new int?[] { trackFile.Id }, Others = new int?[] { trackFile.Id } });
        }
    }
}
