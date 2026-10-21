using System;
using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Cloud;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.Exclusions;
using NzbDrone.Core.ImportLists.ImportListItems;
using NzbDrone.Core.ImportLists.Spotify;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.SkyHook.Resource;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.ThingiProvider.Events;
using SpotifyAPI.Web;
using SpotifyAPI.Web.Models;

namespace NzbDrone.Core.Test.ImportListTests
{
    // The real fetch service, item store, status service, artist and album services over the test database; HTTP, the list factory and config are mocked
    [TestFixture]
    public class ImportListCleanLibraryDbFixture : DbTest
    {
        private const int ListId = 1;

        private ImportListDefinition _definition;
        private SpotifyFollowedArtists _spotifyList;
        private bool _mappingApiDown;
        private bool _listDeleted;

        [SetUp]
        public void SetUp()
        {
            _mappingApiDown = false;
            _listDeleted = false;

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

            Mocker.SetConstant<ILidarrCloudRequestBuilder>(new LidarrCloudRequestBuilder());
            Mocker.SetConstant<IMetadataRequestBuilder>(Mocker.Resolve<MetadataRequestBuilder>());

            Mocker.GetMock<IBuildArtistPaths>()
                  .Setup(v => v.BuildPath(It.IsAny<Artist>(), It.IsAny<bool>()))
                  .Returns((Artist a, bool b) => a.Path);

            Mocker.GetMock<IConfigService>().SetupGet(v => v.ListSyncLevel).Returns(ListSyncLevelType.KeepAndUnmonitor);

            Mocker.GetMock<ISearchForNewArtist>().Setup(v => v.SearchForNewArtist(It.IsAny<string>())).Returns(new List<Artist>());
            Mocker.GetMock<ISearchForNewAlbum>().Setup(v => v.SearchForNewAlbum(It.IsAny<string>(), It.IsAny<string>())).Returns(new List<Album>());
            Mocker.GetMock<IAddArtistService>().Setup(v => v.AddArtists(It.IsAny<List<Artist>>(), It.IsAny<bool>(), It.IsAny<bool>())).Returns(new List<Artist>());
            Mocker.GetMock<IAddAlbumService>().Setup(v => v.AddAlbums(It.IsAny<List<Album>>(), It.IsAny<bool>(), It.IsAny<bool>())).Returns(new List<Album>());
            Mocker.GetMock<IImportListExclusionService>().Setup(v => v.All()).Returns(new List<ImportListExclusion>());

            _definition = new ImportListDefinition
            {
                Id = ListId,
                Name = "Spotify",
                Enable = true,
                EnableAutomaticAdd = true,
                Settings = new SpotifyFollowedArtistsSettings { AccessToken = "a", RefreshToken = "r", Expires = DateTime.UtcNow.AddDays(1) }
            };

            _spotifyList = Mocker.Resolve<SpotifyFollowedArtists>();
            _spotifyList.Definition = _definition;

            Mocker.GetMock<IImportListFactory>().Setup(v => v.Get(ListId)).Returns(_definition);
            Mocker.GetMock<IImportListFactory>().Setup(v => v.Find(ListId)).Returns(() => _listDeleted ? null : _definition);
            Mocker.GetMock<IImportListFactory>().Setup(v => v.All()).Returns(new List<ImportListDefinition> { _definition });
            Mocker.GetMock<IImportListFactory>().Setup(v => v.GetInstance(_definition)).Returns(_spotifyList);
            Mocker.GetMock<IImportListFactory>().Setup(v => v.AutomaticAddEnabled(It.IsAny<bool>())).Returns(new List<IImportList> { _spotifyList });

            // Spotify itself works: the user follows one artist
            Mocker.GetMock<ISpotifyProxy>()
                  .Setup(x => x.GetFollowedArtists(It.IsAny<SpotifyFollowedArtists>(), It.IsAny<SpotifyWebAPI>()))
                  .Returns(new FollowedArtists
                  {
                      Artists = new CursorPaging<FullArtist>
                      {
                          Items = new List<FullArtist> { new FullArtist { Name = "Joe Hisaishi", Id = "sp-hisaishi" } }
                      }
                  });

            // api.lidarr.audio's spotify mapping: up or down
            Mocker.GetMock<IHttpClient>()
                  .Setup(x => x.Post<List<SpotifyMap>>(It.IsAny<HttpRequest>()))
                  .Returns<HttpRequest>(r =>
                  {
                      if (_mappingApiDown)
                      {
                          throw new HttpException(r, new HttpResponse(r, new HttpHeader(), Array.Empty<byte>(), System.Net.HttpStatusCode.ServiceUnavailable));
                      }

                      var map = new List<SpotifyMap> { new SpotifyMap { SpotifyId = "sp-hisaishi", MusicbrainzId = "mb-hisaishi" } };
                      return new HttpResponse<List<SpotifyMap>>(new HttpResponse(r, new HttpHeader(), map.ToJson()));
                  });

            Mocker.GetMock<IHttpClient>()
                  .Setup(x => x.Get<ArtistResource>(It.IsAny<HttpRequest>()))
                  .Throws(new Exception("api.lidarr.audio is down"));
        }

        // MusicBrainz names Japanese artists in their own script; Spotify uses the romanised name
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

        private Artist Reload(Artist artist)
        {
            return Mocker.Resolve<ArtistRepository>().Get(artist.Id);
        }

        private void Sync()
        {
            Mocker.Resolve<ImportListSyncService>().Execute(new ImportListSyncCommand(ListId));
        }

        [Test]
        public void should_not_unmonitor_a_followed_artist_while_the_spotify_mapping_is_down()
        {
            var artist = GivenLibraryArtist("久石譲", "mb-hisaishi");

            Sync();
            Mocker.Resolve<IImportListItemService>().All().Should().ContainSingle(i => i.ArtistMusicBrainzId == "mb-hisaishi");

            // Spotify still returns the same followed artist; only the MusicBrainz mapping is down
            _mappingApiDown = true;
            Sync();

            Reload(artist).Monitored.Should().BeTrue();
            Mocker.Resolve<IImportListItemService>().All().Should().ContainSingle(i => i.ArtistMusicBrainzId == "mb-hisaishi");
        }

        // Spotify's bulk map sets only the album id, and a soundtrack the metadata profile filters out isn't in the library
        [Test]
        public void should_keep_the_artist_of_a_listed_album_that_is_not_in_the_library()
        {
            var artist = GivenLibraryArtist("久石譲", "mb-hisaishi");

            var stored = new List<ImportListItemInfo>
            {
                new ImportListItemInfo { ImportListId = ListId, Artist = "Joe Hisaishi", Album = "Spirited Away", AlbumMusicBrainzId = "rg-spirited-away" }
            };
            Mocker.Resolve<IImportListItemService>().SyncItemsForList(stored, ListId);
            Mocker.Resolve<IImportListStatusService>().UpdateListSyncStatus(ListId, true);

            Mocker.Resolve<ImportListSyncService>().HandleAsync(new ProviderDeletedEvent<IImportList>(99));

            Reload(artist).Monitored.Should().BeTrue();
        }

        [Test]
        public void should_not_store_the_items_of_a_list_deleted_during_its_fetch()
        {
            Mocker.GetMock<ISpotifyProxy>()
                  .Setup(x => x.GetFollowedArtists(It.IsAny<SpotifyFollowedArtists>(), It.IsAny<SpotifyWebAPI>()))
                  .Returns(() =>
                  {
                      _listDeleted = true;
                      var deleted = new ProviderDeletedEvent<IImportList>(ListId);
                      Mocker.Resolve<ImportListItemService>().HandleAsync(deleted);
                      Mocker.Resolve<ImportListStatusService>().HandleAsync(deleted);

                      return new FollowedArtists
                      {
                          Artists = new CursorPaging<FullArtist>
                          {
                              Items = new List<FullArtist> { new FullArtist { Name = "Joe Hisaishi", Id = "sp-hisaishi" } }
                          }
                      };
                  });

            Mocker.Resolve<IFetchAndParseImportList>().FetchSingleList(_definition);

            Mocker.Resolve<IImportListItemService>().GetAllForLists(new List<int> { ListId }).Should().BeEmpty();
        }

        // Spotify's dates have no zone; on a host that isn't on UTC they were stored shifted and rewritten on every sync
        [Test]
        public void should_not_rewrite_unchanged_items_on_the_next_sync()
        {
            var service = Mocker.Resolve<IImportListItemService>();

            List<ImportListItemInfo> Fetch() => new List<ImportListItemInfo>
            {
                new ImportListItemInfo { ImportListId = ListId, Artist = "A", Album = "B", AlbumMusicBrainzId = "rg", ReleaseDate = new DateTime(2020, 5, 1) },
                new ImportListItemInfo { ImportListId = ListId, Artist = "C", Album = "D", AlbumMusicBrainzId = "rg2", ReleaseDate = new DateTime(2021, 5, 1, 0, 0, 0, DateTimeKind.Utc) },
                new ImportListItemInfo { ImportListId = ListId, Artist = "E", Album = "F", AlbumMusicBrainzId = "rg3", ReleaseDate = new DateTime(2022, 5, 1, 0, 0, 0, DateTimeKind.Local) }
            };

            service.SyncItemsForList(Fetch(), ListId);
            var first = service.All().OrderBy(i => i.Artist).Select(i => i.ReleaseDate.ToUniversalTime()).ToList();

            service.SyncItemsForList(Fetch(), ListId);

            first.Should().Equal(new DateTime(2020, 5, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2021, 5, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2022, 5, 1, 0, 0, 0, DateTimeKind.Local).ToUniversalTime());
            service.All().OrderBy(i => i.Artist).Select(i => i.ReleaseDate.ToUniversalTime()).Should().Equal(first);
        }

        [Test]
        public void should_not_count_a_case_only_change_as_a_removal()
        {
            var service = Mocker.Resolve<IImportListItemService>();
            service.SyncItemsForList(new List<ImportListItemInfo> { new ImportListItemInfo { ImportListId = ListId, Artist = "Sigur Rós", ArtistMusicBrainzId = "mb-sr" } }, ListId);

            // Same artist, the provider changed the case of the name
            service.SyncItemsForList(new List<ImportListItemInfo> { new ImportListItemInfo { ImportListId = ListId, Artist = "Sigur rós", ArtistMusicBrainzId = "mb-sr" } }, ListId)
                   .Should().Be(0);
        }

        [Test]
        public void should_clean_with_the_artist_the_sync_mapped_an_album_item_to()
        {
            var listed = GivenLibraryArtist("久石譲", "mb-hisaishi");
            var unlisted = GivenLibraryArtist("Not Listed", "mb-unlisted");

            var albumItem = new ImportListItemInfo { ImportListId = ListId, Artist = "Joe Hisaishi", Album = "Spirited Away", AlbumMusicBrainzId = "rg-spirited-away" };
            var droppedItem = new ImportListItemInfo { ImportListId = ListId, Artist = "Dropped", ArtistMusicBrainzId = "mb-dropped" };
            var items = new List<ImportListItemInfo> { albumItem, droppedItem };

            var list = new Mock<IImportList>();
            list.SetupGet(l => l.Definition).Returns(_definition);
            list.Setup(l => l.Fetch()).Returns(() => items.Select(i => new ImportListItemInfo { ImportListId = ListId, Artist = i.Artist, ArtistMusicBrainzId = i.ArtistMusicBrainzId, Album = i.Album, AlbumMusicBrainzId = i.AlbumMusicBrainzId }).ToList());
            Mocker.GetMock<IImportListFactory>().Setup(v => v.GetInstance(_definition)).Returns(list.Object);

            Mocker.GetMock<ISearchForNewAlbum>()
                  .Setup(v => v.SearchForNewAlbum("lidarr:rg-spirited-away", "Joe Hisaishi"))
                  .Returns(new List<Album>
                  {
                      new Album
                      {
                          ForeignAlbumId = "rg-spirited-away",
                          Title = "Spirited Away",
                          ArtistMetadata = new ArtistMetadata { ForeignArtistId = "mb-hisaishi", Name = "久石譲" }
                      }
                  });

            Sync();
            Mocker.Resolve<IImportListItemService>().All().Should().ContainSingle(i => i.MappedArtistMusicBrainzId == "mb-hisaishi");

            items.Remove(droppedItem);
            Sync();

            Reload(listed).Monitored.Should().BeTrue();
            Reload(unlisted).Monitored.Should().BeFalse();
        }

        [Test]
        public void should_clean_again_once_the_spotify_mapping_is_back()
        {
            var unfollowedLater = GivenLibraryArtist("Unfollowed Later", "mb-later");
            var followed = new List<FullArtist> { new FullArtist { Name = "Joe Hisaishi", Id = "sp-hisaishi" }, new FullArtist { Name = "Unfollowed Later", Id = "sp-later" } };

            Mocker.GetMock<ISpotifyProxy>()
                  .Setup(x => x.GetFollowedArtists(It.IsAny<SpotifyFollowedArtists>(), It.IsAny<SpotifyWebAPI>()))
                  .Returns(() => new FollowedArtists { Artists = new CursorPaging<FullArtist> { Items = followed.ToList() } });
            Mocker.GetMock<IHttpClient>()
                  .Setup(x => x.Post<List<SpotifyMap>>(It.IsAny<HttpRequest>()))
                  .Returns<HttpRequest>(r =>
                  {
                      if (_mappingApiDown)
                      {
                          throw new HttpException(r, new HttpResponse(r, new HttpHeader(), Array.Empty<byte>(), System.Net.HttpStatusCode.ServiceUnavailable));
                      }

                      var map = new List<SpotifyMap> { new SpotifyMap { SpotifyId = "sp-hisaishi", MusicbrainzId = "mb-hisaishi" }, new SpotifyMap { SpotifyId = "sp-later", MusicbrainzId = "mb-later" } };
                      return new HttpResponse<List<SpotifyMap>>(new HttpResponse(r, new HttpHeader(), map.ToJson()));
                  });

            Sync();

            _mappingApiDown = true;
            Sync();

            _mappingApiDown = false;
            followed.RemoveAt(1);
            Sync();

            Reload(unfollowedLater).Monitored.Should().BeFalse();
        }
    }
}
