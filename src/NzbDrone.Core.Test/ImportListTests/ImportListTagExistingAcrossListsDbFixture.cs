using System;
using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.Exclusions;
using NzbDrone.Core.ImportLists.ImportListItems;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ImportListTests
{
    // The real fetch, status, item, artist and album services over the test database; only each list's fetch is mocked
    [TestFixture]
    public class ImportListTagExistingAcrossListsDbFixture : DbTest
    {
        private ImportListDefinition _list1;
        private ImportListDefinition _list2;
        private List<ImportListItemInfo> _items1;
        private List<ImportListItemInfo> _items2;
        private List<Artist> _added;

        [SetUp]
        public void SetUp()
        {
            _items1 = new List<ImportListItemInfo>();
            _items2 = new List<ImportListItemInfo>();
            _added = null;

            Mocker.SetConstant<ICacheManager>(new CacheManager());
            Mocker.SetConstant<IImportListItemRepository>(Mocker.Resolve<ImportListItemRepository>());
            Mocker.SetConstant<IImportListItemService>(Mocker.Resolve<ImportListItemService>());
            Mocker.SetConstant<IImportListStatusRepository>(Mocker.Resolve<ImportListStatusRepository>());
            Mocker.SetConstant<IImportListStatusService>(Mocker.Resolve<ImportListStatusService>());
            Mocker.SetConstant<IArtistMetadataRepository>(Mocker.Resolve<ArtistMetadataRepository>());
            Mocker.SetConstant<IArtistRepository>(Mocker.Resolve<ArtistRepository>());
            Mocker.SetConstant<IArtistService>(Mocker.Resolve<ArtistService>());
            Mocker.SetConstant<IAlbumRepository>(Mocker.Resolve<AlbumRepository>());
            Mocker.SetConstant<IAlbumService>(Mocker.Resolve<AlbumService>());
            Mocker.SetConstant<IFetchAndParseImportList>(Mocker.Resolve<FetchAndParseImportListService>());

            Mocker.GetMock<IConfigService>().SetupGet(v => v.ListSyncLevel).Returns(ListSyncLevelType.Disabled);
            Mocker.GetMock<IImportListExclusionService>().Setup(v => v.All()).Returns(new List<ImportListExclusion>());
            Mocker.GetMock<ISearchForNewArtist>().Setup(v => v.SearchForNewArtist(It.IsAny<string>())).Returns(new List<Artist>());
            Mocker.GetMock<ISearchForNewAlbum>().Setup(v => v.SearchForNewAlbum(It.IsAny<string>(), It.IsAny<string>())).Returns(new List<Album>());
            Mocker.GetMock<IAddArtistService>()
                  .Setup(v => v.AddArtists(It.IsAny<List<Artist>>(), It.IsAny<bool>(), It.IsAny<bool>()))
                  .Returns<List<Artist>, bool, bool>((l, a, b) =>
                  {
                      _added = l.ToList();
                      return new List<Artist>();
                  });
            Mocker.GetMock<IAddAlbumService>().Setup(v => v.AddAlbums(It.IsAny<List<Album>>(), It.IsAny<bool>(), It.IsAny<bool>())).Returns(new List<Album>());

            _list1 = new ImportListDefinition { Id = 1, Name = "One", EnableAutomaticAdd = true, TagExisting = TagExistingType.Add, Tags = new HashSet<int> { 1 } };
            _list2 = new ImportListDefinition { Id = 2, Name = "Two", EnableAutomaticAdd = true, TagExisting = TagExistingType.Add, Tags = new HashSet<int> { 2 } };

            var instance1 = GivenList(_list1, () => _items1);
            var instance2 = GivenList(_list2, () => _items2);

            Mocker.GetMock<IImportListFactory>().Setup(v => v.All()).Returns(() => new List<ImportListDefinition> { _list1, _list2 });
            Mocker.GetMock<IImportListFactory>().Setup(v => v.Find(It.IsAny<int>())).Returns<int>(id => new[] { _list1, _list2 }.FirstOrDefault(l => l.Id == id));
            Mocker.GetMock<IImportListFactory>().Setup(v => v.AutomaticAddEnabled(It.IsAny<bool>())).Returns(new List<IImportList> { instance1, instance2 });
        }

        private IImportList GivenList(ImportListDefinition definition, Func<List<ImportListItemInfo>> items)
        {
            var instance = new Mock<IImportList>();
            instance.SetupGet(v => v.Definition).Returns(definition);
            instance.SetupGet(v => v.Name).Returns(definition.Name);
            instance.SetupGet(v => v.MinRefreshInterval).Returns(TimeSpan.Zero);
            instance.Setup(v => v.Fetch()).Returns(() => items().Select(i => new ImportListItemInfo { ImportListId = definition.Id, Artist = i.Artist, ArtistMusicBrainzId = i.ArtistMusicBrainzId }).ToList());
            return instance.Object;
        }

        private Artist GivenLibraryArtist(string name, string foreignId)
        {
            var metadata = Builder<ArtistMetadata>.CreateNew()
                .With(a => a.Id = 0)
                .With(a => a.Name = name)
                .With(a => a.ForeignArtistId = foreignId)
                .With(a => a.OldForeignArtistIds = new List<string>())
                .BuildNew();
            Mocker.Resolve<ArtistMetadataRepository>().Insert(metadata);

            var artist = Builder<Artist>.CreateNew()
                .With(a => a.Id = 0)
                .With(a => a.ArtistMetadataId = metadata.Id)
                .With(a => a.CleanName = Parser.Parser.CleanArtistName(name))
                .With(a => a.Monitored = true)
                .With(a => a.Tags = new HashSet<int>())
                .BuildNew();
            Mocker.Resolve<ArtistRepository>().Insert(artist);

            return artist;
        }

        private void Sync()
        {
            Mocker.Resolve<ImportListSyncService>().Execute(new ImportListSyncCommand());
        }

        [Test]
        public void should_give_an_existing_artist_on_two_lists_both_lists_tags()
        {
            var kept = GivenLibraryArtist("Kept", "mb-kept");

            _items1.Add(new ImportListItemInfo { Artist = "Kept", ArtistMusicBrainzId = "mb-kept" });
            _items2.Add(new ImportListItemInfo { Artist = "Kept", ArtistMusicBrainzId = "mb-kept" });

            Sync();

            Mocker.Resolve<ArtistRepository>().Get(kept.Id).Tags.Should().BeEquivalentTo(new[] { 1, 2 });
        }

        [Test]
        public void should_give_a_new_artist_both_lists_tags_when_both_list_it()
        {
            _items1.Add(new ImportListItemInfo { Artist = "New", ArtistMusicBrainzId = "mb-new" });
            _items2.Add(new ImportListItemInfo { Artist = "New", ArtistMusicBrainzId = "mb-new" });

            Sync();

            _added.Should().ContainSingle();
            _added.Single().Tags.Should().BeEquivalentTo(new[] { 1, 2 });
        }

        [Test]
        public void should_keep_a_sync_lists_tag_on_an_artist_another_list_also_lists()
        {
            var kept = GivenLibraryArtist("Kept", "mb-kept");
            kept.Tags = new HashSet<int> { 2 };
            Mocker.Resolve<ArtistRepository>().Update(kept);
            _list2.TagExisting = TagExistingType.Sync;

            _items1.Add(new ImportListItemInfo { Artist = "Kept", ArtistMusicBrainzId = "mb-kept" });
            _items2.Add(new ImportListItemInfo { Artist = "Kept", ArtistMusicBrainzId = "mb-kept" });

            Sync();
            Sync();

            Mocker.Resolve<ArtistRepository>().Get(kept.Id).Tags.Should().Contain(2);
        }

        // List 1 has automatic add off but still stores the artist, which Clean Library counts as listed
        [Test]
        public void should_keep_the_tag_for_an_artist_on_a_list_with_automatic_add_off()
        {
            var kept = GivenLibraryArtist("Kept", "mb-kept");
            GivenLibraryArtist("Other", "mb-other");
            kept.Tags = new HashSet<int> { 2 };
            Mocker.Resolve<ArtistRepository>().Update(kept);

            _list1.EnableAutomaticAdd = false;
            _list1.Tags = new HashSet<int> { 2 };
            Mocker.Resolve<IImportListItemService>().SyncItemsForList(new List<ImportListItemInfo> { new ImportListItemInfo { ImportListId = 1, Artist = "Kept", ArtistMusicBrainzId = "mb-kept" } }, 1);
            Mocker.Resolve<IImportListStatusService>().UpdateListSyncStatus(1, false);

            _list2.TagExisting = TagExistingType.Sync;
            _items2.Add(new ImportListItemInfo { Artist = "Other", ArtistMusicBrainzId = "mb-other" });
            var instance2 = GivenList(_list2, () => _items2);
            Mocker.GetMock<IImportListFactory>().Setup(v => v.AutomaticAddEnabled(It.IsAny<bool>())).Returns(new List<IImportList> { instance2 });

            Sync();

            Mocker.Resolve<ArtistRepository>().Get(kept.Id).Tags.Should().Contain(2);
        }
    }
}
