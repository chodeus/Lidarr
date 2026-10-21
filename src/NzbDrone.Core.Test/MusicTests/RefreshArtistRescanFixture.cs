using System;
using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Core.AutoTagging;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.History;
using NzbDrone.Core.ImportLists.Exclusions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Music;
using NzbDrone.Core.Music.Commands;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MusicTests
{
    // Through the real repository: a mocked GetArtists returning a short list hid that it throws for a missing id
    [TestFixture]
    public class RefreshArtistRescanFixture : DbTest<RefreshArtistService, Artist>
    {
        private ArtistService _artistService;
        private List<RescanFoldersCommand> _rescans;

        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<ICacheManager>(new CacheManager());
            Mocker.SetConstant<IArtistRepository>(Mocker.Resolve<ArtistRepository>());
            _artistService = Mocker.Resolve<ArtistService>();
            Mocker.SetConstant<IArtistService>(_artistService);
            Mocker.SetConstant<IAlbumRepository>(Mocker.Resolve<AlbumRepository>());
            Mocker.SetConstant<IAlbumService>(Mocker.Resolve<AlbumService>());

            Mocker.GetMock<IArtistMetadataService>().Setup(s => s.UpsertMany(It.IsAny<List<ArtistMetadata>>())).Returns(true);
            Mocker.GetMock<IImportListExclusionService>().Setup(s => s.FindByForeignId(It.IsAny<List<string>>())).Returns(new List<ImportListExclusion>());
            Mocker.GetMock<IAutoTaggingService>().Setup(s => s.GetTagChanges(It.IsAny<Artist>())).Returns(new AutoTaggingChanges());
            Mocker.GetMock<IHistoryService>().Setup(s => s.GetByArtist(It.IsAny<int>(), It.IsAny<EntityHistoryEventType?>())).Returns(new List<EntityHistory>());
            Mocker.GetMock<IConfigService>().SetupGet(s => s.RescanAfterRefresh).Returns(RescanAfterRefreshType.Always);
            Mocker.GetMock<IRootFolderService>().Setup(s => s.All()).Returns(new List<RootFolder> { new RootFolder { Path = "/music".AsOsAgnostic() } });
            Mocker.GetMock<IRootFolderService>().Setup(s => s.GetBestRootFolder(It.IsAny<string>())).Returns(new RootFolder { Path = "/music".AsOsAgnostic() });
            Mocker.GetMock<IMediaFileService>().Setup(s => s.GetFilesByArtist(It.IsAny<int>())).Returns(new List<TrackFile>());

            _rescans = new List<RescanFoldersCommand>();
            Mocker.GetMock<IManageCommandQueue>()
                  .Setup(q => q.Push(It.IsAny<RescanFoldersCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()))
                  .Callback<RescanFoldersCommand, CommandPriority, CommandTrigger>((c, p, t) => _rescans.Add(c));
        }

        private Artist GivenArtist(string foreignArtistId)
        {
            var metadata = Builder<ArtistMetadata>.CreateNew().With(m => m.Id = 0).With(m => m.ForeignArtistId = foreignArtistId).With(m => m.Status = ArtistStatusType.Continuing).Build();
            Db.Insert(metadata);

            var artist = Builder<Artist>.CreateNew()
                                        .With(a => a.Id = 0)
                                        .With(a => a.ArtistMetadataId = metadata.Id)
                                        .With(a => a.Path = ("/music/" + foreignArtistId).AsOsAgnostic())
                                        .With(a => a.Tags = new HashSet<int>())
                                        .With(a => a.LastInfoSync = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))
                                        .Build();
            Db.Insert(artist);
            artist.Metadata = metadata;

            return artist;
        }

        private void GivenLookup(string foreignArtistId, Artist answer)
        {
            Mocker.GetMock<IProvideArtistInfo>()
                  .Setup(s => s.GetArtistInfo(foreignArtistId, It.IsAny<int>()))
                  .Returns(() => new Artist { CleanName = foreignArtistId, SortName = foreignArtistId, Metadata = answer.Metadata.Value, Albums = new List<Album>() });
        }

        [Test]
        public void should_rescan_root_folders_and_only_stored_artists_when_a_refreshed_artist_was_merged_away()
        {
            var duplicate = GivenArtist("artist-old");
            var target = GivenArtist("artist-target");
            var other = GivenArtist("artist-other");
            GivenLookup("artist-old", target);
            GivenLookup("artist-other", other);

            Subject.Execute(new RefreshArtistCommand(new List<int> { duplicate.Id, other.Id }) { Trigger = CommandTrigger.Manual });

            ExceptionVerification.ExpectedWarns(1);
            var rescan = _rescans.Should().ContainSingle().Subject;
            new { rescan.Folders, rescan.ArtistIds }.Should().BeEquivalentTo(new { Folders = new List<string> { "/music".AsOsAgnostic() }, ArtistIds = new List<int> { other.Id } });
            _artistService.GetArtists(rescan.ArtistIds).Select(a => a.Id).Should().Equal(other.Id);
        }

        [Test]
        public void should_leave_an_artist_merged_away_during_a_full_refresh_out_of_its_rescan()
        {
            var duplicate = GivenArtist("artist-old");
            var target = GivenArtist("artist-target");
            var other = GivenArtist("artist-other");
            GivenLookup("artist-old", target);
            GivenLookup("artist-target", target);
            GivenLookup("artist-other", other);

            Subject.Execute(new RefreshArtistCommand { Trigger = CommandTrigger.Manual });

            ExceptionVerification.ExpectedWarns(1);
            var rescan = _rescans.Should().ContainSingle().Subject;
            rescan.ArtistIds.Should().BeEquivalentTo(new[] { target.Id, other.Id });
            _artistService.GetArtists(rescan.ArtistIds).Should().HaveCount(2);
        }

        [Test]
        public void should_rescan_root_folders_when_a_new_artist_was_merged_away()
        {
            var duplicate = GivenArtist("artist-old");
            var target = GivenArtist("artist-target");
            var other = GivenArtist("artist-other");
            GivenLookup("artist-old", target);
            GivenLookup("artist-other", other);

            Subject.Execute(new RefreshArtistCommand(new List<int> { duplicate.Id, other.Id }, true));

            ExceptionVerification.ExpectedWarns(1);
            var rescan = _rescans.Should().ContainSingle().Subject;
            new { rescan.Folders, rescan.ArtistIds }.Should().BeEquivalentTo(new { Folders = new List<string> { "/music".AsOsAgnostic() }, ArtistIds = new List<int> { other.Id } });
        }

        [Test]
        public void should_rescan_a_new_artist_in_its_own_folder()
        {
            var artist = GivenArtist("artist-1");
            GivenLookup("artist-1", artist);

            Subject.Execute(new RefreshArtistCommand(new List<int> { artist.Id }, true));

            _rescans.Should().ContainSingle().Which.Folders.Should().Equal(artist.Path);
        }

        [Test]
        public void should_scan_an_artist_named_twice_once()
        {
            var artist = GivenArtist("artist-1");
            GivenLookup("artist-1", artist);

            Subject.Execute(new RefreshArtistCommand(new List<int> { artist.Id, artist.Id }) { Trigger = CommandTrigger.Manual });

            var rescan = _rescans.Should().ContainSingle().Subject;
            new { rescan.Folders, rescan.ArtistIds }.Should().BeEquivalentTo(new { Folders = new List<string> { artist.Path }, ArtistIds = new List<int> { artist.Id } });
            _artistService.GetArtists(rescan.ArtistIds).Should().ContainSingle();
        }

        // Auto-tagging runs after the refresh's own save, so a move then isn't put back by it
        [Test]
        public void should_rescan_an_artist_at_the_path_it_was_moved_to_during_its_refresh()
        {
            var artist = GivenArtist("artist-1");
            GivenLookup("artist-1", artist);
            Mocker.GetMock<IAutoTaggingService>()
                  .Setup(s => s.GetTagChanges(It.IsAny<Artist>()))
                  .Callback(() =>
                  {
                      var moved = _artistService.GetArtist(artist.Id);
                      moved.Path = "/music/Moved Artist".AsOsAgnostic();
                      _artistService.UpdateArtist(moved);
                  })
                  .Returns(new AutoTaggingChanges());

            Subject.Execute(new RefreshArtistCommand(new List<int> { artist.Id }) { Trigger = CommandTrigger.Manual });

            _rescans.Should().ContainSingle().Which.Folders.Should().Equal("/music/Moved Artist".AsOsAgnostic());
        }

        [Test]
        public void should_rescan_only_the_artists_folders_when_every_refreshed_artist_is_still_stored()
        {
            var artist = GivenArtist("artist-1");
            GivenLookup("artist-1", artist);

            Subject.Execute(new RefreshArtistCommand(new List<int> { artist.Id }) { Trigger = CommandTrigger.Manual });

            _rescans.Should().ContainSingle().Which.Folders.Should().Equal(artist.Path);
        }
    }
}
