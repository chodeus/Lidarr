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
    // The real artist, album and item services over the test database; the list fetch, metadata search, adds and config are mocked
    [TestFixture]
    public class ImportListTagExistingDbFixture : DbTest
    {
        private List<ImportListItemInfo> _items;
        private ImportListDefinition _list;
        private List<Artist> _added;

        [SetUp]
        public void SetUp()
        {
            _items = new List<ImportListItemInfo>();
            _added = null;

            Mocker.SetConstant<ICacheManager>(new CacheManager());
            Mocker.SetConstant<IImportListItemRepository>(Mocker.Resolve<ImportListItemRepository>());
            Mocker.SetConstant<IImportListItemService>(Mocker.Resolve<ImportListItemService>());
            Mocker.SetConstant<IArtistMetadataRepository>(Mocker.Resolve<ArtistMetadataRepository>());
            Mocker.SetConstant<IArtistRepository>(Mocker.Resolve<ArtistRepository>());
            Mocker.SetConstant<IArtistService>(Mocker.Resolve<ArtistService>());
            Mocker.SetConstant<IAlbumRepository>(Mocker.Resolve<AlbumRepository>());
            Mocker.SetConstant<IAlbumService>(Mocker.Resolve<AlbumService>());

            Mocker.GetMock<IConfigService>().SetupGet(v => v.ListSyncLevel).Returns(ListSyncLevelType.Disabled);
            Mocker.GetMock<IImportListExclusionService>().Setup(v => v.All()).Returns(new List<ImportListExclusion>());
            Mocker.GetMock<IAddArtistService>()
                  .Setup(v => v.AddArtists(It.IsAny<List<Artist>>(), It.IsAny<bool>(), It.IsAny<bool>()))
                  .Returns<List<Artist>, bool, bool>((l, a, b) =>
                  {
                      _added = l.ToList();
                      return new List<Artist>();
                  });
            Mocker.GetMock<IAddAlbumService>().Setup(v => v.AddAlbums(It.IsAny<List<Album>>(), It.IsAny<bool>(), It.IsAny<bool>())).Returns(new List<Album>());
            Mocker.GetMock<ISearchForNewArtist>().Setup(v => v.SearchForNewArtist(It.IsAny<string>())).Returns(new List<Artist>());

            _list = new ImportListDefinition { Id = 1, Name = "List", EnableAutomaticAdd = true, TagExisting = true, Tags = new HashSet<int> { 7 } };

            var instance = new Mock<IImportList>();
            instance.SetupGet(v => v.Definition).Returns(_list);
            Mocker.GetMock<IImportListFactory>().Setup(v => v.All()).Returns(() => new List<ImportListDefinition> { _list });
            Mocker.GetMock<IImportListFactory>().Setup(v => v.AutomaticAddEnabled(It.IsAny<bool>())).Returns(new List<IImportList> { instance.Object });
            Mocker.GetMock<IFetchAndParseImportList>().Setup(v => v.Fetch()).Returns(() => _items);
        }

        private Artist GivenLibraryArtist(string name, string foreignId)
        {
            var metadata = GivenMetadata(name, foreignId);

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

        private ArtistMetadata GivenMetadata(string name, string foreignId)
        {
            var metadata = Builder<ArtistMetadata>.CreateNew()
                .With(a => a.Id = 0)
                .With(a => a.Name = name)
                .With(a => a.ForeignArtistId = foreignId)
                .With(a => a.OldForeignArtistIds = new List<string>())
                .BuildNew();
            Mocker.Resolve<ArtistMetadataRepository>().Insert(metadata);

            return metadata;
        }

        private void Sync()
        {
            Mocker.Resolve<ImportListSyncService>().Execute(new ImportListSyncCommand());
        }

        // A refresh merging artists, or the user, deletes one while the sync searches for a later item
        [Test]
        public void should_still_add_new_artists_and_tag_the_rest_when_a_listed_artist_is_deleted_during_the_sync()
        {
            var deleted = GivenLibraryArtist("Gone", "mb-gone");
            var kept = GivenLibraryArtist("Kept", "mb-kept");

            _items.Add(new ImportListItemInfo { ImportListId = 1, Artist = "Gone", ArtistMusicBrainzId = "mb-gone" });
            _items.Add(new ImportListItemInfo { ImportListId = 1, Artist = "Kept", ArtistMusicBrainzId = "mb-kept" });
            _items.Add(new ImportListItemInfo { ImportListId = 1, Artist = "New Artist" });

            Mocker.GetMock<ISearchForNewArtist>()
                  .Setup(v => v.SearchForNewArtist("New Artist"))
                  .Returns(() =>
                  {
                      Mocker.Resolve<IArtistService>().DeleteArtist(deleted.Id, false);
                      return new List<Artist> { new Artist { Metadata = new ArtistMetadata { ForeignArtistId = "mb-new", Name = "New Artist" } } };
                  });

            Sync();

            _added.Should().ContainSingle(a => a.ForeignArtistId == "mb-new");
            Mocker.Resolve<ArtistRepository>().Get(kept.Id).Tags.Should().Contain(7);
        }

        [Test]
        public void should_add_new_artists_and_tag_existing_ones()
        {
            var kept = GivenLibraryArtist("Kept", "mb-kept");

            _items.Add(new ImportListItemInfo { ImportListId = 1, Artist = "Kept", ArtistMusicBrainzId = "mb-kept" });
            _items.Add(new ImportListItemInfo { ImportListId = 1, Artist = "New Artist", ArtistMusicBrainzId = "mb-new" });

            Sync();

            _added.Should().ContainSingle(a => a.ForeignArtistId == "mb-new");
            Mocker.Resolve<ArtistRepository>().Get(kept.Id).Tags.Should().Contain(7);
        }

        [Test]
        public void should_still_add_new_artists_when_a_listed_album_has_no_artist_in_the_library()
        {
            var orphanMetadata = GivenMetadata("Orphan", "mb-orphan");
            var album = Builder<Album>.CreateNew()
                .With(a => a.Id = 0)
                .With(a => a.ForeignAlbumId = "mb-album")
                .With(a => a.ArtistMetadataId = orphanMetadata.Id)
                .With(a => a.OldForeignAlbumIds = new List<string>())
                .BuildNew();
            Mocker.Resolve<AlbumRepository>().Insert(album);

            _items.Add(new ImportListItemInfo { ImportListId = 1, Album = "Album", AlbumMusicBrainzId = "mb-album", ArtistMusicBrainzId = "mb-orphan" });
            _items.Add(new ImportListItemInfo { ImportListId = 1, Artist = "New Artist", ArtistMusicBrainzId = "mb-new" });

            Sync();

            _added.Should().ContainSingle(a => a.ForeignArtistId == "mb-new");
        }
    }
}
