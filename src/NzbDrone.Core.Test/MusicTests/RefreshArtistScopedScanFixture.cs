using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Disk;
using NzbDrone.Core.AutoTagging;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.History;
using NzbDrone.Core.ImportLists.Exclusions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.MediaFiles.TrackImport;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Music;
using NzbDrone.Core.Music.Commands;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MusicTests
{
    // The refresh's rescan runs through the real DiskScanService, root folders and media files over the test database
    [TestFixture]
    public class RefreshArtistScopedScanFixture : DbTest<RefreshArtistService, Artist>
    {
        private List<RescanFoldersCommand> _rescans;

        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<ICacheManager>(new CacheManager());
            Mocker.SetConstant<IArtistRepository>(Mocker.Resolve<ArtistRepository>());
            Mocker.SetConstant<IArtistService>(Mocker.Resolve<ArtistService>());
            Mocker.SetConstant<IAlbumRepository>(Mocker.Resolve<AlbumRepository>());
            Mocker.SetConstant<IAlbumService>(Mocker.Resolve<AlbumService>());
            Mocker.SetConstant<IRootFolderRepository>(Mocker.Resolve<RootFolderRepository>());
            Mocker.SetConstant<IRootFolderService>(Mocker.Resolve<RootFolderService>());
            Mocker.SetConstant<IMediaFileRepository>(Mocker.Resolve<MediaFileRepository>());
            Mocker.SetConstant<IMediaFileService>(Mocker.Resolve<MediaFileService>());
            Mocker.SetConstant<IMediaFileTableCleanupService>(Mocker.Resolve<MediaFileTableCleanupService>());

            Mocker.GetMock<IArtistMetadataService>().Setup(s => s.UpsertMany(It.IsAny<List<ArtistMetadata>>())).Returns(true);
            Mocker.GetMock<IImportListExclusionService>().Setup(s => s.FindByForeignId(It.IsAny<List<string>>())).Returns(new List<ImportListExclusion>());
            Mocker.GetMock<IAutoTaggingService>().Setup(s => s.GetTagChanges(It.IsAny<Artist>())).Returns(new AutoTaggingChanges());
            Mocker.GetMock<IHistoryService>().Setup(s => s.GetByArtist(It.IsAny<int>(), It.IsAny<EntityHistoryEventType?>())).Returns(new List<EntityHistory>());
            Mocker.GetMock<IConfigService>().SetupGet(s => s.RescanAfterRefresh).Returns(RescanAfterRefreshType.Always);
            Mocker.GetMock<ITrackService>().Setup(s => s.GetTracksByFileId(It.IsAny<IEnumerable<int>>())).Returns(new List<Track>());

            Db.Insert(Builder<RootFolder>.CreateNew().With(r => r.Id = 0).With(r => r.Path = "/music".AsOsAgnostic()).Build());

            Mocker.GetMock<IDiskProvider>().Setup(d => d.FolderExists(It.IsAny<string>())).Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.GetFileInfos(It.IsAny<string>(), It.IsAny<bool>())).Returns(new List<IFileInfo>());
            Mocker.GetMock<IMakeImportDecision>()
                  .Setup(s => s.GetImportDecisions(It.IsAny<List<IFileInfo>>(), It.IsAny<IdentificationOverrides>(), It.IsAny<ImportDecisionMakerInfo>(), It.IsAny<ImportDecisionMakerConfig>()))
                  .Returns(new List<ImportDecision<LocalTrack>>());

            _rescans = new List<RescanFoldersCommand>();
            Mocker.GetMock<IManageCommandQueue>()
                  .Setup(q => q.Push(It.IsAny<RescanFoldersCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()))
                  .Callback<RescanFoldersCommand, CommandPriority, CommandTrigger>((c, p, t) => _rescans.Add(c));
        }

        private Artist GivenArtist(string foreignArtistId, string path)
        {
            var metadata = Builder<ArtistMetadata>.CreateNew().With(m => m.Id = 0).With(m => m.ForeignArtistId = foreignArtistId).With(m => m.Status = ArtistStatusType.Continuing).Build();
            Db.Insert(metadata);

            var artist = Builder<Artist>.CreateNew()
                                        .With(a => a.Id = 0)
                                        .With(a => a.ArtistMetadataId = metadata.Id)
                                        .With(a => a.Path = path.AsOsAgnostic())
                                        .With(a => a.Tags = new HashSet<int>())
                                        .With(a => a.LastInfoSync = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))
                                        .Build();
            Db.Insert(artist);
            artist.Metadata = metadata;

            Mocker.GetMock<IProvideArtistInfo>()
                  .Setup(s => s.GetArtistInfo(foreignArtistId, It.IsAny<int>()))
                  .Returns(() => new Artist { CleanName = foreignArtistId, SortName = foreignArtistId, Metadata = metadata, Albums = new List<Album>() });

            return artist;
        }

        private TrackFile GivenTrackFile(Artist artist, string path)
        {
            var album = Builder<Album>.CreateNew().With(a => a.Id = 0).With(a => a.ArtistMetadataId = artist.ArtistMetadataId).Build();
            Db.Insert(album);

            var release = Builder<AlbumRelease>.CreateNew().With(r => r.Id = 0).With(r => r.AlbumId = album.Id).With(r => r.Monitored = true).Build();
            Db.Insert(release);

            var file = Builder<TrackFile>.CreateNew().With(f => f.Id = 0).With(f => f.AlbumId = album.Id).With(f => f.Path = path.AsOsAgnostic()).With(f => f.Quality = new QualityModel(Quality.FLAC)).Build();
            Db.Insert(file);

            Db.Insert(Builder<Track>.CreateNew().With(t => t.Id = 0).With(t => t.AlbumReleaseId = release.Id).With(t => t.TrackFileId = file.Id).Build());

            return file;
        }

        private List<string> ScannedFolders()
        {
            var rescan = _rescans.Should().ContainSingle().Subject;
            Mocker.Resolve<DiskScanService>().Execute(rescan);

            return Mocker.GetMock<IDiskProvider>().Invocations.Where(i => i.Method.Name == nameof(IDiskProvider.GetFileInfos)).Select(i => (string)i.Arguments[0]).ToList();
        }

        [Test]
        public void should_still_scan_the_other_artists_when_a_chosen_artist_is_outside_every_root_folder()
        {
            var orphan = GivenArtist("artist-orphan", "/removed-root/Orphan");
            var normal = GivenArtist("artist-normal", "/music/Normal");

            Subject.Execute(new BulkRefreshArtistCommand(new List<int> { orphan.Id, normal.Id }) { Trigger = CommandTrigger.Manual });

            ScannedFolders().Should().Equal(normal.Path);
            _rescans.Single().ArtistIds.Should().Equal(normal.Id);

            ExceptionVerification.ExpectedWarns(2);
        }

        [Test]
        public void should_scan_the_root_folders_when_the_artist_has_files_outside_its_folder()
        {
            var artist = GivenArtist("artist-1", "/music/Artist New");
            var stale = GivenTrackFile(artist, "/music/Artist Old/01.flac");

            // The scan skips the clean-up of a folder with no audio files, as for an unmounted drive
            var onDisk = new Mock<IFileInfo>();
            onDisk.SetupGet(f => f.FullName).Returns("/music/Another Artist/01.flac".AsOsAgnostic());
            onDisk.SetupGet(f => f.Name).Returns("01.flac");
            onDisk.SetupGet(f => f.Extension).Returns(".flac");
            Mocker.GetMock<IDiskProvider>().Setup(d => d.GetFileInfos("/music".AsOsAgnostic(), It.IsAny<bool>())).Returns(new List<IFileInfo> { onDisk.Object });

            Subject.Execute(new RefreshArtistCommand(new List<int> { artist.Id }) { Trigger = CommandTrigger.Manual });

            ScannedFolders().Should().Equal("/music".AsOsAgnostic());
            Db.All<TrackFile>().Select(f => f.Path).Should().NotContain(stale.Path);
        }

        [Test]
        public void should_scan_only_the_artists_folder_when_its_files_are_inside_it()
        {
            var artist = GivenArtist("artist-1", "/music/Artist");
            GivenTrackFile(artist, "/music/Artist/Album/01.flac");

            Subject.Execute(new RefreshArtistCommand(new List<int> { artist.Id }) { Trigger = CommandTrigger.Manual });

            ScannedFolders().Should().Equal(artist.Path);

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_not_push_a_rescan_with_no_folders()
        {
            var orphan = GivenArtist("artist-orphan", "/removed-root/Orphan");

            Subject.Execute(new BulkRefreshArtistCommand(new List<int> { orphan.Id }) { Trigger = CommandTrigger.Manual });

            _rescans.Should().BeEmpty();
            ExceptionVerification.ExpectedWarns(1);
        }
    }
}
