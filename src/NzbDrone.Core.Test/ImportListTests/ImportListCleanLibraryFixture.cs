using System;
using System.Collections.Generic;
using System.Linq;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.ImportListItems;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.ThingiProvider.Events;

namespace NzbDrone.Core.Test.ImportListTests
{
    [TestFixture]
    public class ImportListCleanLibraryFixture : CoreTest<ImportListSyncService>
    {
        private List<IImportList> _importLists;
        private List<Artist> _existingArtists;
        private List<Album> _existingAlbums;
        private List<ImportListItemInfo> _listItems;

        [SetUp]
        public void SetUp()
        {
            _importLists = new List<IImportList>();
            _listItems = new List<ImportListItemInfo>();

            _existingArtists = Enumerable.Range(1, 3).Select(i => new Artist
            {
                Id = i,
                ArtistMetadataId = i,
                CleanName = $"Artist {i}".CleanArtistName(),
                Monitored = true,
                Tags = new HashSet<int>(),
                Metadata = new LazyLoaded<ArtistMetadata>(new ArtistMetadata
                {
                    ForeignArtistId = $"artist-{i}",
                    OldForeignArtistIds = new List<string> { $"old-artist-{i}" }
                })
            }).ToList();

            _existingAlbums = _existingArtists.Select(a => new Album
            {
                ArtistMetadataId = a.ArtistMetadataId,
                ForeignAlbumId = $"album-{a.Id}",
                OldForeignAlbumIds = new List<string> { $"old-album-{a.Id}" }
            }).ToList();

            Mocker.GetMock<IImportListFactory>()
                  .Setup(v => v.AutomaticAddEnabled(It.IsAny<bool>()))
                  .Returns(_importLists);

            Mocker.GetMock<IFetchAndParseImportList>()
                  .Setup(v => v.Fetch())
                  .Returns(new List<ImportListItemInfo>());

            Mocker.GetMock<IFetchAndParseImportList>()
                  .Setup(v => v.FetchSingleList(It.IsAny<ImportListDefinition>()))
                  .Returns(new List<ImportListItemInfo>());

            Mocker.GetMock<IArtistService>()
                  .Setup(v => v.GetAllArtists())
                  .Returns(_existingArtists);

            Mocker.GetMock<IAlbumService>()
                  .Setup(v => v.GetAllAlbums())
                  .Returns(_existingAlbums);

            Mocker.GetMock<IImportListItemService>()
                  .Setup(v => v.All())
                  .Returns(_listItems);
        }

        private void WithCleanLevel(ListSyncLevelType cleanLevel, int? tagId = null)
        {
            Mocker.GetMock<IConfigService>()
                  .SetupGet(v => v.ListSyncLevel)
                  .Returns(cleanLevel);

            if (tagId.HasValue)
            {
                Mocker.GetMock<IConfigService>()
                      .SetupGet(v => v.ListSyncTag)
                      .Returns(tagId.Value);
            }
        }

        private void WithList(int id, bool pendingRemovals = true, DateTime? disabledTill = null, bool neverSynced = false)
        {
            var importListDefinition = new ImportListDefinition { Id = id, EnableAutomaticAdd = true };

            Mocker.GetMock<IImportListFactory>()
                  .Setup(v => v.Get(id))
                  .Returns(importListDefinition);

            var mockImportList = new Mock<IImportList>();
            mockImportList.SetupGet(s => s.Definition).Returns(importListDefinition);

            Mocker.GetMock<IImportListStatusService>()
                  .Setup(v => v.GetListStatus(id))
                  .Returns(new ImportListStatus
                  {
                      LastInfoSync = neverSynced ? null : DateTime.UtcNow,
                      HasRemovedItemSinceLastClean = pendingRemovals,
                      DisabledTill = disabledTill
                  });

            _importLists.Add(mockImportList.Object);
        }

        private void VerifyUnmonitored(params int[] artistIds)
        {
            Mocker.GetMock<IArtistService>()
                  .Verify(v => v.UpdateArtists(It.Is<List<Artist>>(l => l.Select(a => a.Id).SequenceEqual(artistIds) && l.All(a => !a.Monitored)), true), Times.Once());
        }

        private void VerifyNotCleaned()
        {
            Mocker.GetMock<IArtistService>()
                  .Verify(v => v.GetAllArtists(), Times.Never());

            Mocker.GetMock<IArtistService>()
                  .Verify(v => v.UpdateArtists(It.IsAny<List<Artist>>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void should_not_clean_library_if_lists_have_not_removed_any_items()
        {
            WithList(1, pendingRemovals: false);
            WithCleanLevel(ListSyncLevelType.KeepAndUnmonitor);

            Subject.Execute(new ImportListSyncCommand());

            VerifyNotCleaned();
        }

        [Test]
        public void should_clean_library_if_any_list_has_removed_items()
        {
            WithList(1);
            WithList(2, pendingRemovals: false);
            WithCleanLevel(ListSyncLevelType.KeepAndUnmonitor);

            Subject.Execute(new ImportListSyncCommand());

            VerifyUnmonitored(1, 2, 3);
        }

        [Test]
        public void should_not_clean_library_if_config_value_disable()
        {
            WithList(1);
            WithCleanLevel(ListSyncLevelType.Disabled);

            Subject.Execute(new ImportListSyncCommand());

            VerifyNotCleaned();
        }

        [Test]
        public void should_not_clean_if_list_failures()
        {
            WithList(1);
            WithList(2, disabledTill: DateTime.UtcNow.AddHours(1));
            WithCleanLevel(ListSyncLevelType.KeepAndUnmonitor);

            Subject.Execute(new ImportListSyncCommand());

            VerifyNotCleaned();
        }

        [Test]
        public void should_not_clean_if_a_list_has_never_synced()
        {
            WithList(1);
            WithList(2, neverSynced: true);
            WithCleanLevel(ListSyncLevelType.KeepAndUnmonitor);

            Subject.Execute(new ImportListSyncCommand());

            VerifyNotCleaned();
        }

        [Test]
        public void should_log_only_on_clean_library_if_config_value_logonly()
        {
            WithList(1);
            WithCleanLevel(ListSyncLevelType.LogOnly);

            Subject.Execute(new ImportListSyncCommand());

            Mocker.GetMock<IArtistService>()
                  .Verify(v => v.UpdateArtists(It.Is<List<Artist>>(l => l.Count == 0), true), Times.Once());

            Mocker.GetMock<IImportListStatusService>()
                  .Verify(v => v.MarkListsAsCleaned(), Times.Once());
        }

        [Test]
        public void should_unmonitor_on_clean_library_if_config_value_keepAndUnmonitor()
        {
            _existingArtists[1].Monitored = false;
            WithList(1);
            WithCleanLevel(ListSyncLevelType.KeepAndUnmonitor);

            Subject.Execute(new ImportListSyncCommand());

            VerifyUnmonitored(1, 3);

            Mocker.GetMock<IImportListStatusService>()
                  .Verify(v => v.MarkListsAsCleaned(), Times.Once());
        }

        [Test]
        public void should_tag_artist_on_clean_library_if_config_value_keepAndTag()
        {
            _existingArtists[1].Tags.Add(4);
            WithList(1);
            WithCleanLevel(ListSyncLevelType.KeepAndTag, 4);

            Subject.Execute(new ImportListSyncCommand());

            Mocker.GetMock<IArtistService>()
                  .Verify(v => v.UpdateArtists(It.Is<List<Artist>>(l => l.Select(a => a.Id).SequenceEqual(new[] { 1, 3 }) && l.All(a => a.Tags.Contains(4) && a.Monitored)), true), Times.Once());
        }

        [TestCase("artist-1", null, null)]
        [TestCase("old-artist-1", null, null)]
        [TestCase(null, "Artist 1", null)]
        [TestCase(null, "ARTIST 1", null)]
        [TestCase(null, null, "album-1")]
        [TestCase(null, null, "old-album-1")]
        public void should_not_clean_artist_that_is_on_a_list(string artistId, string artistName, string albumId)
        {
            _listItems.Add(new ImportListItemInfo { ImportListId = 1, ArtistMusicBrainzId = artistId, Artist = artistName, AlbumMusicBrainzId = albumId });
            WithList(1);
            WithCleanLevel(ListSyncLevelType.KeepAndUnmonitor);

            Subject.Execute(new ImportListSyncCommand());

            VerifyUnmonitored(2, 3);
        }

        [Test]
        public void should_clean_after_syncing_a_single_list()
        {
            WithList(1);
            WithCleanLevel(ListSyncLevelType.KeepAndUnmonitor);

            Subject.Execute(new ImportListSyncCommand(1));

            VerifyUnmonitored(1, 2, 3);
        }

        [Test]
        public void should_try_to_clean_when_a_list_is_deleted()
        {
            WithList(1);
            WithCleanLevel(ListSyncLevelType.KeepAndUnmonitor);

            Subject.HandleAsync(new ProviderDeletedEvent<IImportList>(2));

            VerifyUnmonitored(1, 2, 3);
        }
    }
}
