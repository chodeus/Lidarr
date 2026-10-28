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
            Mocker.GetMock<IConfigService>().SetupGet(c => c.ExtraFileExtensions).Returns("lrc,log");

            Mocker.SetConstant<IExtraFileRepository<LyricFile>>(Mocker.Resolve<LyricFileRepository>());
            Mocker.SetConstant<ILyricFileService>(Mocker.Resolve<LyricFileService>());
            Mocker.SetConstant<IExtraFileRepository<OtherExtraFile>>(Mocker.Resolve<OtherExtraFileRepository>());
            Mocker.SetConstant<IOtherExtraFileService>(Mocker.Resolve<OtherExtraFileService>());

            _recycled = new List<string>();
            Mocker.GetMock<IRecycleBinProvider>()
                  .Setup(s => s.DeleteFile(It.IsAny<string>(), It.IsAny<string>()))
                  .Callback<string, string>((path, subfolder) =>
                  {
                      _recycled.Add(Path.GetFileName(path));
                      File.Delete(path);
                  });

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

            // The new file lands in the album folder under its download name
            Mocker.GetMock<IUpgradeMediaFiles>()
                  .Setup(s => s.UpgradeTrackFile(It.IsAny<TrackFile>(), It.IsAny<LocalTrack>(), It.IsAny<bool>()))
                  .Returns<TrackFile, LocalTrack, bool>((f, l, c) =>
                  {
                      var folder = Path.Combine(_artist.Path, "Album");
                      Directory.CreateDirectory(folder);
                      f.Path = Path.Combine(folder, Path.GetFileName(l.Path));
                      File.Copy(l.Path, f.Path);
                      return new TrackFileMoveResult { TrackFile = f };
                  });
        }

        private List<ImportResult> Import(string fileName)
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

            return Subject.Import(new List<ImportDecision<LocalTrack>> { decision }, false, new DownloadClientItem { DownloadId = "dl-1", CanMoveFiles = false });
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
