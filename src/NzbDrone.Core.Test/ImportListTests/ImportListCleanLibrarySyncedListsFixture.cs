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
using NzbDrone.Core.ImportLists.LastFm;
using NzbDrone.Core.ImportLists.Spotify;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.SkyHook.Resource;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using SpotifyAPI.Web;
using SpotifyAPI.Web.Models;

namespace NzbDrone.Core.Test.ImportListTests
{
    // The real sync, fetch, item store, status, artist and album services over the test database
    [TestFixture]
    public class ImportListCleanLibrarySyncedListsFixture : DbTest
    {
        private ImportListDefinition _definition;
        private ImportListDefinition _manualDefinition;
        private List<ImportListItemInfo> _items;

        [SetUp]
        public void SetUp()
        {
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

            _definition = new ImportListDefinition { Id = 1, Name = "Auto", Enable = true, EnableAutomaticAdd = true };
            _manualDefinition = new ImportListDefinition { Id = 2, Name = "Manual", Enable = true, EnableAutomaticAdd = false };

            _items = new List<ImportListItemInfo>();

            var list = new Mock<IImportList>();
            list.SetupGet(l => l.Definition).Returns(_definition);
            list.Setup(l => l.Fetch()).Returns(() => _items.Select(i => new ImportListItemInfo { ImportListId = 1, Artist = i.Artist, ArtistMusicBrainzId = i.ArtistMusicBrainzId, Album = i.Album, AlbumMusicBrainzId = i.AlbumMusicBrainzId }).ToList());

            Mocker.GetMock<IImportListFactory>().Setup(v => v.Get(1)).Returns(_definition);
            Mocker.GetMock<IImportListFactory>().Setup(v => v.Find(1)).Returns(_definition);
            Mocker.GetMock<IImportListFactory>().Setup(v => v.Find(2)).Returns(_manualDefinition);
            Mocker.GetMock<IImportListFactory>().Setup(v => v.All()).Returns(new List<ImportListDefinition> { _definition, _manualDefinition });
            Mocker.GetMock<IImportListFactory>().Setup(v => v.GetInstance(_definition)).Returns(list.Object);
            Mocker.GetMock<IImportListFactory>().Setup(v => v.AutomaticAddEnabled(It.IsAny<bool>())).Returns(new List<IImportList> { list.Object });
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

        private Artist Reload(Artist artist)
        {
            return Mocker.Resolve<ArtistRepository>().Get(artist.Id);
        }

        private void Sync()
        {
            Mocker.Resolve<ImportListSyncService>().Execute(new ImportListSyncCommand(1));
        }

        // Last.fm sends "mbid": "" for artists it has no MusicBrainz id for
        private static List<ImportListItemInfo> ParseLastFmTopAlbums()
        {
            var json = "{\"topalbums\":{\"album\":[{\"name\":\"OK Computer\",\"mbid\":\"\",\"artist\":{\"name\":\"Radiohead\",\"mbid\":\"\"}}]}}";
            var request = new ImportListRequest("http://ws.audioscrobbler.com/2.0/", HttpAccept.Json);
            var response = new HttpResponse(request.HttpRequest, new HttpHeader(), json);

            return new LastFmParser().ParseResponse(new ImportListResponse(request, response)).ToList();
        }

        // Last.fm names albums by title only, so the sync can only place this item by a search
        [Test]
        public void should_not_clean_while_an_item_is_placed_only_by_a_title_search()
        {
            GivenLibraryArtist("Radiohead", "mb-radiohead");
            var unlisted = GivenLibraryArtist("Not Listed", "mb-unlisted");

            Mocker.GetMock<ISearchForNewAlbum>()
                  .Setup(v => v.SearchForNewAlbum("OK Computer", "Radiohead"))
                  .Returns(new List<Album>
                  {
                      new Album { ForeignAlbumId = "rg-ok-computer", Title = "OK Computer", ArtistMetadata = new ArtistMetadata { ForeignArtistId = "mb-radiohead", Name = "Radiohead" } }
                  });

            var dropped = new ImportListItemInfo { Artist = "Dropped", ArtistMusicBrainzId = "mb-dropped" };
            _items.AddRange(ParseLastFmTopAlbums());
            _items.Add(dropped);

            Sync();
            _items.Remove(dropped);
            Sync();

            Reload(unlisted).Monitored.Should().BeTrue();
            Mocker.Resolve<IImportListItemService>().All().Should().Contain(i => i.Album == "OK Computer" && i.MappedArtistMusicBrainzId == null);
        }

        [Test]
        public void should_not_hold_the_clean_back_for_an_item_of_a_list_that_no_longer_syncs()
        {
            GivenLibraryArtist("Kept", "mb-kept");
            var unlisted = GivenLibraryArtist("Not Listed", "mb-unlisted");

            Mocker.Resolve<IImportListItemService>().SyncItemsForList(new List<ImportListItemInfo> { new ImportListItemInfo { ImportListId = 2, Artist = "Unplaceable Bootleg Artist" } }, 2);

            var dropped = new ImportListItemInfo { Artist = "Dropped", ArtistMusicBrainzId = "mb-dropped" };
            _items.Add(new ImportListItemInfo { Artist = "Kept", ArtistMusicBrainzId = "mb-kept" });
            _items.Add(dropped);

            Sync();
            _items.Remove(dropped);
            Sync();

            Reload(unlisted).Monitored.Should().BeFalse();
        }

        [Test]
        public void should_not_back_off_a_whole_spotify_list_when_one_item_lookup_fails()
        {
            var definition = new ImportListDefinition
            {
                Id = 1,
                Name = "Spotify",
                Enable = true,
                EnableAutomaticAdd = true,
                Settings = new SpotifyFollowedArtistsSettings { AccessToken = "a", RefreshToken = "r", Expires = DateTime.UtcNow.AddDays(1) }
            };
            var spotify = Mocker.Resolve<SpotifyFollowedArtists>();
            spotify.Definition = definition;

            Mocker.GetMock<IImportListFactory>().Setup(v => v.Get(1)).Returns(definition);
            Mocker.GetMock<IImportListFactory>().Setup(v => v.Find(1)).Returns(definition);
            Mocker.GetMock<IImportListFactory>().Setup(v => v.All()).Returns(new List<ImportListDefinition> { definition });
            Mocker.GetMock<IImportListFactory>().Setup(v => v.GetInstance(definition)).Returns(spotify);

            Mocker.GetMock<ISpotifyProxy>()
                  .Setup(x => x.GetFollowedArtists(It.IsAny<SpotifyFollowedArtists>(), It.IsAny<SpotifyWebAPI>()))
                  .Returns(new FollowedArtists
                  {
                      Artists = new CursorPaging<FullArtist>
                      {
                          Items = new List<FullArtist> { new FullArtist { Name = "Mapped", Id = "sp-mapped" }, new FullArtist { Name = "Odd One", Id = "sp-odd" } }
                      }
                  });

            Mocker.GetMock<IHttpClient>()
                  .Setup(x => x.Post<List<SpotifyMap>>(It.IsAny<HttpRequest>()))
                  .Returns<HttpRequest>(r => new HttpResponse<List<SpotifyMap>>(new HttpResponse(r, new HttpHeader(), new List<SpotifyMap> { new SpotifyMap { SpotifyId = "sp-mapped", MusicbrainzId = "mb-mapped" } }.ToJson())));

            Mocker.GetMock<IHttpClient>()
                  .Setup(x => x.Get<ArtistResource>(It.IsAny<HttpRequest>()))
                  .Returns<HttpRequest>(r => new HttpResponse<ArtistResource>(new HttpResponse(r, new HttpHeader(), Array.Empty<byte>(), System.Net.HttpStatusCode.InternalServerError)));

            Sync();

            Mocker.Resolve<IImportListStatusService>().GetBlockedProviders().Should().BeEmpty();
        }

        [Test]
        public void should_not_trust_an_artist_found_by_a_title_search()
        {
            GivenLibraryArtist("Radiohead", "mb-radiohead");
            var unlisted = GivenLibraryArtist("Not Listed", "mb-unlisted");

            Mocker.GetMock<ISearchForNewAlbum>()
                  .Setup(v => v.SearchForNewAlbum("OK Computer", "Radiohead"))
                  .Returns(new List<Album>
                  {
                      new Album { ForeignAlbumId = "rg-ok-computer", Title = "OK Computer", ArtistMetadata = new ArtistMetadata { ForeignArtistId = "mb-radiohead", Name = "Radiohead" } }
                  });

            var dropped = new ImportListItemInfo { Artist = "Dropped", ArtistMusicBrainzId = "mb-dropped" };
            _items.Add(new ImportListItemInfo { Artist = "Radiohead", Album = "OK Computer" });
            _items.Add(dropped);

            Sync();
            _items.Remove(dropped);
            Sync();

            Reload(unlisted).Monitored.Should().BeTrue();
        }
    }
}
