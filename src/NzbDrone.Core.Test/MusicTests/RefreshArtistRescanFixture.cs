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
    // The real ArtistService over the test database; the lookup, the album refresh and auto-tagging are mocked
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

            _rescans = new List<RescanFoldersCommand>();
            Mocker.GetMock<IManageCommandQueue>()
                  .Setup(q => q.Push(It.IsAny<RescanFoldersCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()))
                  .Callback<RescanFoldersCommand, CommandPriority, CommandTrigger>((c, p, t) => _rescans.Add(c));
        }

        private Artist GivenArtist(string foreignArtistId)
        {
            var metadata = Builder<ArtistMetadata>.CreateNew().With(m => m.Id = 0).With(m => m.ForeignArtistId = foreignArtistId).Build();
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
        public void should_rescan_only_the_artists_folders_when_every_refreshed_artist_is_still_stored()
        {
            var artist = GivenArtist("artist-1");
            GivenLookup("artist-1", artist);

            Subject.Execute(new RefreshArtistCommand(new List<int> { artist.Id }) { Trigger = CommandTrigger.Manual });

            _rescans.Should().ContainSingle().Which.Folders.Should().Equal(artist.Path);
        }
    }
}
