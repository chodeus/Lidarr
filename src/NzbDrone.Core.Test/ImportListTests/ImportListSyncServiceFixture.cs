using System;
using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.Exclusions;
using NzbDrone.Core.ImportLists.ImportListItems;
using NzbDrone.Core.IndexerSearch;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ImportListTests
{
    public class ImportListSyncServiceFixture : CoreTest<ImportListSyncService>
    {
        private List<ImportListItemInfo> _importListReports;

        [SetUp]
        public void SetUp()
        {
            var importListItem1 = new ImportListItemInfo
            {
                Artist = "Linkin Park"
            };

            _importListReports = new List<ImportListItemInfo> { importListItem1 };

            var mockImportList = new Mock<IImportList>();

            Mocker.GetMock<ISearchForNewArtist>()
                .Setup(v => v.SearchForNewArtist(It.IsAny<string>()))
                .Returns(new List<Artist>());

            Mocker.GetMock<ISearchForNewAlbum>()
                .Setup(v => v.SearchForNewAlbum(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(new List<Album>());

            Mocker.GetMock<IImportListFactory>()
                .Setup(v => v.All())
                .Returns(new List<ImportListDefinition> { new() { ShouldMonitor = ImportListMonitorType.SpecificAlbum } });

            Mocker.GetMock<IImportListFactory>()
                .Setup(v => v.AutomaticAddEnabled(It.IsAny<bool>()))
                .Returns(new List<IImportList> { mockImportList.Object });

            Mocker.GetMock<IFetchAndParseImportList>()
                .Setup(v => v.Fetch())
                .Returns(_importListReports);

            Mocker.GetMock<IImportListExclusionService>()
                .Setup(v => v.All())
                .Returns(new List<ImportListExclusion>());

            Mocker.GetMock<IAddArtistService>()
                .Setup(v => v.AddArtists(It.IsAny<List<Artist>>(), false, true))
                .Returns((List<Artist> artists, bool doRefresh, bool ignoreErrors) => artists);

            Mocker.GetMock<IAddAlbumService>()
                .Setup(v => v.AddAlbums(It.IsAny<List<Album>>(), false, true))
                .Returns((List<Album> albums, bool doRefresh, bool ignoreErrors) => albums);

            Mocker.GetMock<IImportListStatusService>()
                .Setup(v => v.GetListStatus(It.IsAny<int>()))
                .Returns(new ImportListStatus { LastInfoSync = DateTime.UtcNow });

            Mocker.GetMock<IImportListItemService>()
                .Setup(v => v.GetAllForLists(It.IsAny<List<int>>()))
                .Returns(new List<ImportListItemInfo>());

            Mocker.GetMock<IAlbumService>()
                .Setup(v => v.FindById(It.IsAny<List<string>>()))
                .Returns(new List<Album>());

            Mocker.GetMock<IAlbumService>()
                .Setup(v => v.FindByOldForeignIds(It.IsAny<List<string>>()))
                .Returns(new List<Album>());

            Mocker.GetMock<IArtistService>()
                .Setup(v => v.GetAllArtists())
                .Returns(new List<Artist>());
        }

        private void WithAlbum()
        {
            _importListReports.First().Album = "Meteora";
        }

        private void WithArtistId()
        {
            _importListReports.First().ArtistMusicBrainzId = "f59c5520-5f46-4d2c-b2c4-822eabf53419";
        }

        private void WithAlbumId()
        {
            _importListReports.First().AlbumMusicBrainzId = "09474d62-17dd-3a4f-98fb-04c65f38a479";
        }

        private void WithSecondBook()
        {
            var importListItem2 = new ImportListItemInfo
            {
                Artist = "Linkin Park",
                ArtistMusicBrainzId = "f59c5520-5f46-4d2c-b2c4-822eabf53419",
                Album = "Meteora 2",
                AlbumMusicBrainzId = "madeup"
            };
            _importListReports.Add(importListItem2);
        }

        private void WithExistingArtist(bool monitored)
        {
            Mocker.GetMock<IArtistService>()
                .Setup(v => v.FindById(_importListReports.First().ArtistMusicBrainzId))
                .Returns(new Artist { Id = 1, ForeignArtistId = _importListReports.First().ArtistMusicBrainzId, Monitored = monitored });
        }

        private void WithExistingAlbum(bool monitored)
        {
            var album = Builder<Album>.CreateNew()
                .With(x => x.Id = 1)
                .With(x => x.ForeignAlbumId = _importListReports.First().AlbumMusicBrainzId)
                .With(x => x.Monitored = monitored)
                .Build();

            var artist = Builder<Artist>.CreateNew()
                .With(x => x.Monitored = monitored)
                .With(x => x.ForeignArtistId = _importListReports.First().ArtistMusicBrainzId)
                .With(x => x.Albums = new List<Album> { album })
                .Build();

            album.Artist = artist;

            Mocker.GetMock<IAlbumService>()
                .Setup(v => v.FindById(_importListReports.First().AlbumMusicBrainzId))
                .Returns(album);
        }

        private void WithExcludedArtist()
        {
            Mocker.GetMock<IImportListExclusionService>()
                .Setup(v => v.All())
                .Returns(new List<ImportListExclusion>
                {
                    new ImportListExclusion
                    {
                        ForeignId = "f59c5520-5f46-4d2c-b2c4-822eabf53419"
                    }
                });
        }

        private void WithExcludedAlbum()
        {
            Mocker.GetMock<IImportListExclusionService>()
                .Setup(v => v.All())
                .Returns(new List<ImportListExclusion>
                {
                    new ImportListExclusion
                    {
                        ForeignId = "09474d62-17dd-3a4f-98fb-04c65f38a479"
                    }
                });
        }

        private void WithListSettings(ImportListMonitorType monitor = ImportListMonitorType.EntireArtist, bool shouldMonitorExisting = false, bool shouldSearch = true)
        {
            Mocker.GetMock<IImportListFactory>()
                .Setup(v => v.All())
                .Returns(new List<ImportListDefinition> { new() { ShouldMonitor = monitor, ShouldMonitorExisting = shouldMonitorExisting, ShouldSearch = shouldSearch } });
        }

        [Test]
        public void should_search_if_artist_title_and_no_artist_id()
        {
            Subject.Execute(new ImportListSyncCommand());

            Mocker.GetMock<ISearchForNewArtist>()
                .Verify(v => v.SearchForNewArtist(It.IsAny<string>()), Times.Once());
        }

        [Test]
        public void should_not_search_if_artist_title_and_artist_id()
        {
            WithArtistId();
            Subject.Execute(new ImportListSyncCommand());

            Mocker.GetMock<ISearchForNewArtist>()
                .Verify(v => v.SearchForNewArtist(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_search_if_album_title_and_no_album_id()
        {
            WithAlbum();
            Subject.Execute(new ImportListSyncCommand());

            Mocker.GetMock<ISearchForNewAlbum>()
                .Verify(v => v.SearchForNewAlbum(It.IsAny<string>(), It.IsAny<string>()), Times.Once());
        }

        [Test]
        public void should_not_search_if_album_title_and_album_id()
        {
            WithArtistId();
            WithAlbumId();
            Subject.Execute(new ImportListSyncCommand());

            Mocker.GetMock<ISearchForNewAlbum>()
                .Verify(v => v.SearchForNewAlbum(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_not_search_if_all_info()
        {
            WithArtistId();
            WithAlbum();
            WithAlbumId();
            Subject.Execute(new ImportListSyncCommand());

            Mocker.GetMock<ISearchForNewArtist>()
                .Verify(v => v.SearchForNewArtist(It.IsAny<string>()), Times.Never());

            Mocker.GetMock<ISearchForNewAlbum>()
                .Verify(v => v.SearchForNewAlbum(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_not_add_if_existing_artist()
        {
            WithArtistId();
            WithExistingArtist(false);

            Subject.Execute(new ImportListSyncCommand());

            Mocker.GetMock<IAddArtistService>()
                .Verify(v => v.AddArtists(It.Is<List<Artist>>(t => t.Count == 0), false, It.IsAny<bool>()));
        }

        [Test]
        public void should_not_add_if_existing_album()
        {
            WithAlbumId();
            WithExistingAlbum(false);

            Subject.Execute(new ImportListSyncCommand());

            Mocker.GetMock<IAddArtistService>()
                .Verify(v => v.AddArtists(It.Is<List<Artist>>(t => t.Count == 0), false, It.IsAny<bool>()));
        }

        [Test]
        public void should_add_if_existing_artist_but_new_album()
        {
            WithAlbumId();
            WithArtistId();
            WithExistingArtist(false);

            Subject.Execute(new ImportListSyncCommand());

            Mocker.GetMock<IAddAlbumService>()
                .Verify(v => v.AddAlbums(It.Is<List<Album>>(t => t.Count == 1), false, It.IsAny<bool>()));
        }

        [TestCase(ImportListMonitorType.None, false)]
        [TestCase(ImportListMonitorType.SpecificAlbum, true)]
        [TestCase(ImportListMonitorType.EntireArtist, true)]
        public void should_add_if_not_existing_artist(ImportListMonitorType monitor, bool expectedArtistMonitored)
        {
            WithArtistId();
            WithListSettings(monitor);

            Subject.Execute(new ImportListSyncCommand());

            Mocker.GetMock<IAddArtistService>()
                .Verify(v => v.AddArtists(It.Is<List<Artist>>(t => t.Count == 1 && t.First().Monitored == expectedArtistMonitored), false, It.IsAny<bool>()));
        }

        [TestCase(ImportListMonitorType.None, false)]
        [TestCase(ImportListMonitorType.SpecificAlbum, true)]
        [TestCase(ImportListMonitorType.EntireArtist, true)]
        public void should_add_if_not_existing_album(ImportListMonitorType monitor, bool expectedAlbumMonitored)
        {
            WithAlbumId();
            WithArtistId();
            WithListSettings(monitor);

            Subject.Execute(new ImportListSyncCommand());

            Mocker.GetMock<IAddAlbumService>()
                .Verify(v => v.AddAlbums(It.Is<List<Album>>(t => t.Count == 1 && t.First().Monitored == expectedAlbumMonitored), false, It.IsAny<bool>()));
        }

        [Test]
        public void should_not_add_artist_if_excluded_artist()
        {
            WithArtistId();
            WithExcludedArtist();

            Subject.Execute(new ImportListSyncCommand());

            Mocker.GetMock<IAddArtistService>()
                .Verify(v => v.AddArtists(It.Is<List<Artist>>(t => t.Count == 0), false, It.IsAny<bool>()));
        }

        [Test]
        public void should_not_add_album_if_excluded_album()
        {
            WithAlbumId();
            WithArtistId();
            WithExcludedAlbum();

            Subject.Execute(new ImportListSyncCommand());

            Mocker.GetMock<IAddAlbumService>()
                .Verify(v => v.AddAlbums(It.Is<List<Album>>(t => t.Count == 0), false, It.IsAny<bool>()));
        }

        [Test]
        public void should_not_add_album_if_excluded_artist()
        {
            WithAlbumId();
            WithArtistId();
            WithExcludedArtist();

            Subject.Execute(new ImportListSyncCommand());

            Mocker.GetMock<IAddAlbumService>()
                .Verify(v => v.AddAlbums(It.Is<List<Album>>(t => t.Count == 0), false, It.IsAny<bool>()));
        }

        [TestCase(ImportListMonitorType.None, 0, false)]
        [TestCase(ImportListMonitorType.SpecificAlbum, 2, true)]
        [TestCase(ImportListMonitorType.EntireArtist, 0, true)]
        public void should_add_two_albums(ImportListMonitorType monitor, int expectedAlbumsMonitored, bool expectedArtistMonitored)
        {
            WithAlbum();
            WithAlbumId();
            WithSecondBook();
            WithArtistId();
            WithListSettings(monitor);

            Subject.Execute(new ImportListSyncCommand());

            Mocker.GetMock<IAddAlbumService>()
                .Verify(v => v.AddAlbums(It.Is<List<Album>>(t => t.Count == 2), false, true));
            Mocker.GetMock<IAddArtistService>()
                .Verify(v => v.AddArtists(It.Is<List<Artist>>(t => t.Count == 1 &&
                                                                   t.First().AddOptions.AlbumsToMonitor.Count == expectedAlbumsMonitored &&
                                                                   t.First().Monitored == expectedArtistMonitored),
                    false,
                    true));
        }

        [TestCase(ImportListMonitorType.SpecificAlbum)]
        [TestCase(ImportListMonitorType.EntireArtist)]
        public void should_monitor_existing_unmonitored_album(ImportListMonitorType monitorType)
        {
            WithAlbumId();
            WithArtistId();
            WithExistingAlbum(false);

            WithListSettings(monitorType, true);

            Subject.Execute(new ImportListSyncCommand());

            Mocker.GetMock<IAlbumService>()
                .Verify(v => v.SetAlbumMonitored(1, true));
        }

        [TestCase(ImportListMonitorType.SpecificAlbum)]
        [TestCase(ImportListMonitorType.EntireArtist)]
        public void should_not_monitor_existing_monitored_album(ImportListMonitorType monitorType)
        {
            WithAlbumId();
            WithArtistId();
            WithExistingAlbum(true);

            WithListSettings(monitorType, true);

            Subject.Execute(new ImportListSyncCommand());

            Mocker.GetMock<IAlbumService>()
                .Verify(v => v.SetAlbumMonitored(1, true), Times.Never);
        }

        [TestCase(ImportListMonitorType.SpecificAlbum, false)]
        [TestCase(ImportListMonitorType.EntireArtist, false)]
        [TestCase(ImportListMonitorType.None, false)]
        [TestCase(ImportListMonitorType.None, true)]

        public void should_not_monitor_existing_unmonitored_album(ImportListMonitorType monitorType, bool shouldMonitorExisting)
        {
            WithAlbumId();
            WithArtistId();
            WithExistingAlbum(false);

            WithListSettings(monitorType, shouldMonitorExisting);

            Subject.Execute(new ImportListSyncCommand());

            Mocker.GetMock<IAlbumService>()
                .Verify(v => v.SetAlbumMonitored(1, true), Times.Never);
        }

        [Test]
        public void should_search_specific_existing_unmonitored_album()
        {
            WithAlbumId();
            WithArtistId();
            WithExistingAlbum(false);

            WithListSettings(ImportListMonitorType.SpecificAlbum, true, true);

            Subject.Execute(new ImportListSyncCommand());

            Mocker.GetMock<IManageCommandQueue>()
                .Verify(v => v.Push<Command>(It.Is<AlbumSearchCommand>(x => x.AlbumIds.Count == 1 && x.AlbumIds.Contains(1)), CommandPriority.Normal, CommandTrigger.Unspecified));
        }

        [Test]
        public void should_not_search_specific_existing_unmonitored_album()
        {
            WithAlbumId();
            WithArtistId();
            WithExistingAlbum(false);

            WithListSettings(ImportListMonitorType.SpecificAlbum, true, false);

            Subject.Execute(new ImportListSyncCommand());

            Mocker.GetMock<IManageCommandQueue>()
                .Verify(v => v.Push<Command>(It.Is<AlbumSearchCommand>(x => x.AlbumIds.Count == 1 && x.AlbumIds.Contains(1)), CommandPriority.Normal, CommandTrigger.Unspecified), Times.Never);
        }

        [Test]
        public void should_search_all_artist_albums()
        {
            WithAlbumId();
            WithArtistId();
            WithExistingAlbum(false);

            WithListSettings(ImportListMonitorType.EntireArtist, true, true);

            Subject.Execute(new ImportListSyncCommand());

            Mocker.GetMock<IManageCommandQueue>()
                .Verify(v => v.Push<Command>(It.Is<MissingAlbumSearchCommand>(x => x.ArtistId == 1), CommandPriority.Normal, CommandTrigger.Unspecified));
        }

        [Test]
        public void should_not_search_all_artist_albums()
        {
            WithAlbumId();
            WithArtistId();
            WithExistingAlbum(false);

            WithListSettings(ImportListMonitorType.EntireArtist, true, false);

            Subject.Execute(new ImportListSyncCommand());

            Mocker.GetMock<IManageCommandQueue>()
                .Verify(v => v.Push<Command>(It.Is<MissingAlbumSearchCommand>(x => x.ArtistId == 1), CommandPriority.Normal, CommandTrigger.Unspecified), Times.Never);
        }

        [TestCase(ImportListMonitorType.SpecificAlbum)]
        [TestCase(ImportListMonitorType.EntireArtist)]
        [TestCase(ImportListMonitorType.None)]

        public void should_monitor_existing_unmonitored_artist(ImportListMonitorType monitorType)
        {
            WithArtistId();
            WithExistingArtist(false);

            WithListSettings(monitorType, true);

            Subject.Execute(new ImportListSyncCommand());

            Mocker.GetMock<IArtistService>()
                .Verify(v => v.UpdateArtist(It.Is<Artist>(a => a.Monitored), true));
        }

        [TestCase(ImportListMonitorType.SpecificAlbum)]
        [TestCase(ImportListMonitorType.EntireArtist)]
        [TestCase(ImportListMonitorType.None)]

        public void should_not_monitor_existing_monitored_artist(ImportListMonitorType monitorType)
        {
            WithArtistId();
            WithExistingArtist(true);

            WithListSettings(monitorType, true);

            Subject.Execute(new ImportListSyncCommand());

            Mocker.GetMock<IArtistService>()
                .Verify(v => v.UpdateArtist(It.IsAny<Artist>(), It.IsAny<bool>()), Times.Never);
        }

        [TestCase(ImportListMonitorType.SpecificAlbum)]
        [TestCase(ImportListMonitorType.EntireArtist)]
        [TestCase(ImportListMonitorType.None)]

        public void should_not_monitor_existing_unmonitored_artist(ImportListMonitorType monitorType)
        {
            WithArtistId();
            WithExistingArtist(false);

            WithListSettings(monitorType, false);

            Subject.Execute(new ImportListSyncCommand());

            Mocker.GetMock<IArtistService>()
                .Verify(v => v.UpdateArtist(It.IsAny<Artist>(), It.IsAny<bool>()), Times.Never);
        }

        [Test]
        public void should_search_unmonitored_artist()
        {
            WithArtistId();
            WithExistingArtist(false);

            WithListSettings(ImportListMonitorType.EntireArtist, true, true);

            Subject.Execute(new ImportListSyncCommand());

            Mocker.GetMock<IManageCommandQueue>()
                .Verify(v => v.Push<Command>(It.Is<MissingAlbumSearchCommand>(x => x.ArtistId == 1), CommandPriority.Normal, CommandTrigger.Unspecified));
        }

        [Test]
        public void should_not_search_unmonitored_artist()
        {
            WithArtistId();
            WithExistingArtist(false);

            WithListSettings(ImportListMonitorType.EntireArtist, true, false);

            Subject.Execute(new ImportListSyncCommand());

            Mocker.GetMock<IManageCommandQueue>()
                .Verify(v => v.Push<Command>(It.IsAny<MissingAlbumSearchCommand>(), CommandPriority.Normal, CommandTrigger.Unspecified), Times.Never);
        }

        [Test]
        public void should_not_fetch_if_no_lists_are_enabled()
        {
            Mocker.GetMock<IImportListFactory>()
                .Setup(v => v.AutomaticAddEnabled(It.IsAny<bool>()))
                .Returns(new List<IImportList>());

            Subject.Execute(new ImportListSyncCommand());

            Mocker.GetMock<IFetchAndParseImportList>()
                .Verify(v => v.Fetch(), Times.Never);
        }

        [Test]
        public void should_not_process_if_no_items_are_returned()
        {
            Mocker.GetMock<IFetchAndParseImportList>()
                .Setup(v => v.Fetch())
                .Returns(new List<ImportListItemInfo>());

            Subject.Execute(new ImportListSyncCommand());

            Mocker.GetMock<IImportListExclusionService>()
                .Verify(v => v.All(), Times.Never);
        }

        private void WithTaggingLists(params ImportListDefinition[] lists)
        {
            Mocker.GetMock<IImportListFactory>()
                .Setup(v => v.All())
                .Returns(lists.ToList());
        }

        private static ImportListDefinition TaggingList(int id, TagExistingType tagExisting, params int[] tags)
        {
            return new ImportListDefinition { Id = id, Name = $"List {id}", EnableAutomaticAdd = true, ShouldMonitor = ImportListMonitorType.EntireArtist, TagExisting = tagExisting, Tags = new HashSet<int>(tags) };
        }

        private void WithLibraryArtist(int id, params int[] tags)
        {
            Mocker.GetMock<IArtistService>()
                .Setup(v => v.GetArtist(id))
                .Returns(new Artist { Id = id, Tags = new HashSet<int>(tags) });
        }

        private void VerifyTagged(int artistId, params int[] tags)
        {
            Mocker.GetMock<IArtistService>()
                .Verify(v => v.UpdateTags(It.Is<List<Artist>>(l => l.Single().Id == artistId && l.Single().Tags.SetEquals(tags))), Times.Once());

            Mocker.GetMock<IArtistService>()
                .Verify(v => v.UpdateArtists(It.IsAny<List<Artist>>(), It.IsAny<bool>()), Times.Never());
        }

        private void VerifyNotTagged()
        {
            Mocker.GetMock<IArtistService>()
                .Verify(v => v.UpdateTags(It.IsAny<List<Artist>>()), Times.Never());

            Mocker.GetMock<IArtistService>()
                .Verify(v => v.UpdateArtists(It.IsAny<List<Artist>>(), It.IsAny<bool>()), Times.Never());
        }

        [TestCase(TagExistingType.Add)]
        [TestCase(TagExistingType.Sync)]
        public void should_add_the_lists_tags_to_an_existing_artist_when_tag_existing_is_on(TagExistingType tagExisting)
        {
            WithArtistId();
            WithExistingArtist(true);
            WithLibraryArtist(1, 3);
            WithTaggingLists(TaggingList(0, tagExisting, 1, 2));

            Subject.Execute(new ImportListSyncCommand());

            VerifyTagged(1, 1, 2, 3);
        }

        [Test]
        public void should_not_tag_existing_artists_when_tag_existing_is_off()
        {
            WithArtistId();
            WithExistingArtist(true);
            WithLibraryArtist(1, 3);
            WithTaggingLists(TaggingList(0, TagExistingType.None, 1, 2));

            Subject.Execute(new ImportListSyncCommand());

            VerifyNotTagged();
        }

        [Test]
        public void should_not_update_an_artist_that_already_has_the_lists_tags()
        {
            WithArtistId();
            WithExistingArtist(true);
            WithLibraryArtist(1, 1, 2);
            WithTaggingLists(TaggingList(0, TagExistingType.Add, 1, 2));

            Subject.Execute(new ImportListSyncCommand());

            VerifyNotTagged();
        }

        [Test]
        public void should_add_the_lists_tags_to_the_artist_of_an_existing_album()
        {
            WithAlbumId();
            WithArtistId();
            WithExistingAlbum(true);
            var artistId = Mocker.GetMock<IAlbumService>().Object.FindById(_importListReports.First().AlbumMusicBrainzId).ArtistId;
            WithLibraryArtist(artistId);
            WithTaggingLists(TaggingList(0, TagExistingType.Add, 4));

            Subject.Execute(new ImportListSyncCommand());

            VerifyTagged(artistId, 4);
        }

        [Test]
        public void should_give_a_new_artist_on_two_lists_both_lists_tags_and_leave_the_others_alone()
        {
            const string sharedArtistId = "11111111-1111-1111-1111-111111111111";
            const string otherArtistId = "22222222-2222-2222-2222-222222222222";

            _importListReports.Clear();
            _importListReports.Add(new ImportListItemInfo { ImportListId = 1, Artist = "Artist Name", ArtistMusicBrainzId = sharedArtistId });
            _importListReports.Add(new ImportListItemInfo { ImportListId = 1, Artist = "Other Artist", ArtistMusicBrainzId = otherArtistId });
            _importListReports.Add(new ImportListItemInfo { ImportListId = 2, Artist = "Artist Name", ArtistMusicBrainzId = sharedArtistId });
            WithTaggingLists(TaggingList(1, TagExistingType.None, 1), TaggingList(2, TagExistingType.None, 2));

            Subject.Execute(new ImportListSyncCommand());

            Mocker.GetMock<IAddArtistService>()
                .Verify(v => v.AddArtists(It.Is<List<Artist>>(l => l.Count == 2 && HasTags(l, sharedArtistId, 1, 2) && HasTags(l, otherArtistId, 1)), false, It.IsAny<bool>()));
        }

        [Test]
        public void should_give_the_new_artist_of_an_album_on_two_lists_both_lists_tags()
        {
            WithAlbumId();
            WithArtistId();
            _importListReports.First().ImportListId = 1;
            _importListReports.Add(new ImportListItemInfo { ImportListId = 2, ArtistMusicBrainzId = _importListReports.First().ArtistMusicBrainzId, AlbumMusicBrainzId = _importListReports.First().AlbumMusicBrainzId });
            WithTaggingLists(TaggingList(1, TagExistingType.None, 1), TaggingList(2, TagExistingType.None, 2));

            Subject.Execute(new ImportListSyncCommand());

            Mocker.GetMock<IAddArtistService>()
                .Verify(v => v.AddArtists(It.Is<List<Artist>>(l => l.Single().Tags.SetEquals(new[] { 1, 2 })), false, It.IsAny<bool>()));
        }

        [Test]
        public void should_tag_the_existing_artist_of_a_new_album_on_a_second_list()
        {
            WithAlbumId();
            WithArtistId();
            WithExistingArtist(true);
            WithLibraryArtist(1, 3);
            _importListReports.First().ImportListId = 1;
            _importListReports.Add(new ImportListItemInfo { ImportListId = 2, ArtistMusicBrainzId = _importListReports.First().ArtistMusicBrainzId, AlbumMusicBrainzId = _importListReports.First().AlbumMusicBrainzId });
            WithTaggingLists(TaggingList(1, TagExistingType.None, 1), TaggingList(2, TagExistingType.Add, 2));

            Subject.Execute(new ImportListSyncCommand());

            VerifyTagged(1, 2, 3);
        }

        private static Artist TaggedArtist(int id, string foreignArtistId, params int[] tags)
        {
            return new Artist
            {
                Id = id,
                CleanName = foreignArtistId,
                Metadata = new ArtistMetadata { ForeignArtistId = foreignArtistId, OldForeignArtistIds = new List<string>() },
                Tags = new HashSet<int>(tags)
            };
        }

        private void WithSyncLists(params ImportListDefinition[] lists)
        {
            _importListReports.First().ImportListId = lists.First().Id;
            WithTaggingLists(lists);
        }

        private void WithLibrary(params Artist[] artists)
        {
            Mocker.GetMock<IArtistService>()
                .Setup(v => v.GetAllArtists())
                .Returns(artists.ToList());

            foreach (var artist in artists)
            {
                Mocker.GetMock<IArtistService>()
                    .Setup(v => v.GetArtist(artist.Id))
                    .Returns(artist);
            }
        }

        private void WithStoredItems(params ImportListItemInfo[] items)
        {
            Mocker.GetMock<IImportListItemService>()
                .Setup(v => v.GetAllForLists(It.IsAny<List<int>>()))
                .Returns((List<int> ids) => items.Where(i => ids.Contains(i.ImportListId)).ToList());
        }

        private static ImportListItemInfo StoredArtist(int listId, string foreignArtistId)
        {
            return new ImportListItemInfo { ImportListId = listId, Artist = foreignArtistId, ArtistMusicBrainzId = foreignArtistId };
        }

        private void VerifyUntagged(params Artist[] artists)
        {
            Mocker.GetMock<IArtistService>()
                .Verify(v => v.UpdateTags(It.Is<List<Artist>>(l => l.Select(a => a.Id).OrderBy(i => i).SequenceEqual(artists.Select(a => a.Id).OrderBy(i => i)))), Times.Once());
        }

        [Test]
        public void should_remove_a_sync_lists_tag_from_an_artist_no_longer_on_it()
        {
            var onList = TaggedArtist(1, "mb-on-list", 7, 3);
            var offList = TaggedArtist(2, "mb-off-list", 7, 3);
            WithLibrary(onList, offList);
            WithStoredItems(StoredArtist(1, "mb-on-list"));
            WithSyncLists(TaggingList(1, TagExistingType.Sync, 7));

            Subject.Execute(new ImportListSyncCommand());

            VerifyUntagged(offList);
            offList.Tags.Should().BeEquivalentTo(new[] { 3 });
            onList.Tags.Should().BeEquivalentTo(new[] { 7, 3 });
        }

        [Test]
        public void should_keep_a_tag_added_after_the_library_was_read()
        {
            var cached = TaggedArtist(1, "mb-off-list", 7);
            var current = TaggedArtist(1, "mb-off-list", 7, 9);
            WithLibrary(cached);
            Mocker.GetMock<IArtistService>().Setup(v => v.GetArtist(1)).Returns(current);
            WithStoredItems(StoredArtist(1, "mb-on-list"));
            WithSyncLists(TaggingList(1, TagExistingType.Sync, 7));

            Subject.Execute(new ImportListSyncCommand());

            Mocker.GetMock<IArtistService>()
                .Verify(v => v.UpdateTags(It.Is<List<Artist>>(l => l.Single() == current && current.Tags.SetEquals(new[] { 9 }))), Times.Once());
            cached.Tags.Should().BeEquivalentTo(new[] { 7 });
        }

        [Test]
        public void should_skip_an_artist_deleted_after_the_library_was_read()
        {
            WithLibrary(TaggedArtist(1, "mb-off-list", 7));
            Mocker.GetMock<IArtistService>().Setup(v => v.GetArtist(1)).Throws(new ModelNotFoundException(typeof(Artist), 1));
            WithStoredItems(StoredArtist(1, "mb-on-list"));
            WithSyncLists(TaggingList(1, TagExistingType.Sync, 7));

            Subject.Execute(new ImportListSyncCommand());

            VerifyNotTagged();
        }

        [Test]
        public void should_not_remove_tags_when_the_list_only_adds_them()
        {
            WithLibrary(TaggedArtist(1, "mb-off-list", 7));
            WithStoredItems(StoredArtist(1, "mb-on-list"));
            WithSyncLists(TaggingList(1, TagExistingType.Add, 7));

            Subject.Execute(new ImportListSyncCommand());

            VerifyNotTagged();
        }

        [Test]
        public void should_keep_the_tag_while_the_artist_is_on_another_list_with_it()
        {
            var first = TaggedArtist(1, "mb-first", 7);
            var second = TaggedArtist(2, "mb-second", 7);
            var neither = TaggedArtist(3, "mb-neither", 7);
            WithLibrary(first, second, neither);
            WithStoredItems(StoredArtist(1, "mb-first"), StoredArtist(2, "mb-second"));
            WithSyncLists(TaggingList(1, TagExistingType.Sync, 7), TaggingList(2, TagExistingType.None, 7));

            Subject.Execute(new ImportListSyncCommand());

            VerifyUntagged(neither);
        }

        [TestCase(false, false, false)]
        [TestCase(true, true, false)]
        [TestCase(true, false, true)]
        public void should_keep_the_tag_while_a_list_with_it_has_not_synced(bool synced, bool disabled, bool failedSinceSync)
        {
            WithLibrary(TaggedArtist(1, "mb-off-list", 7));
            WithStoredItems(StoredArtist(1, "mb-on-list"), StoredArtist(2, "mb-on-list"));
            WithSyncLists(TaggingList(1, TagExistingType.Sync, 7), TaggingList(2, TagExistingType.None, 7));

            var lastSync = DateTime.UtcNow.AddHours(-1);
            Mocker.GetMock<IImportListStatusService>()
                .Setup(v => v.GetListStatus(2))
                .Returns(new ImportListStatus
                {
                    LastInfoSync = synced ? lastSync : null,
                    DisabledTill = disabled ? DateTime.UtcNow.AddHours(1) : null,
                    MostRecentFailure = failedSinceSync ? lastSync.AddMinutes(30) : null
                });

            Subject.Execute(new ImportListSyncCommand());

            VerifyNotTagged();
        }

        [TestCase(TagExistingType.Sync)]
        [TestCase(TagExistingType.None)]
        public void should_keep_the_tag_while_a_list_with_it_has_no_items(TagExistingType emptyListMode)
        {
            WithLibrary(TaggedArtist(1, "mb-off-list", 7));
            WithStoredItems(StoredArtist(1, "mb-on-list"));
            WithSyncLists(TaggingList(1, TagExistingType.Sync, 7), TaggingList(2, emptyListMode, 7));

            Subject.Execute(new ImportListSyncCommand());

            VerifyNotTagged();
        }

        [Test]
        public void should_not_remove_tags_that_no_sync_list_has()
        {
            WithLibrary(TaggedArtist(1, "mb-off-list", 3));
            WithStoredItems(StoredArtist(1, "mb-on-list"));
            WithSyncLists(TaggingList(1, TagExistingType.Sync, 7));

            Subject.Execute(new ImportListSyncCommand());

            VerifyNotTagged();
        }

        [Test]
        public void should_keep_the_tag_while_a_list_item_is_not_matched_to_an_artist()
        {
            WithLibrary(TaggedArtist(1, "mb-off-list", 7));
            WithStoredItems(StoredArtist(1, "mb-on-list"), new ImportListItemInfo { ImportListId = 1, Artist = "Unmatched Name" });
            WithSyncLists(TaggingList(1, TagExistingType.Sync, 7));

            Subject.Execute(new ImportListSyncCommand());

            VerifyNotTagged();
        }

        [Test]
        public void should_keep_the_tag_for_the_artist_of_a_listed_album()
        {
            var albumArtist = TaggedArtist(1, "mb-album-artist", 7);
            albumArtist.ArtistMetadataId = 11;
            WithLibrary(albumArtist);
            WithStoredItems(new ImportListItemInfo { ImportListId = 1, Album = "Album Title", AlbumMusicBrainzId = "mb-album", ArtistMusicBrainzId = "mb-credited" });
            WithSyncLists(TaggingList(1, TagExistingType.Sync, 7));

            Mocker.GetMock<IAlbumService>()
                .Setup(v => v.FindById(It.IsAny<List<string>>()))
                .Returns(new List<Album> { new Album { ForeignAlbumId = "mb-album", ArtistMetadataId = 11, OldForeignAlbumIds = new List<string>() } });

            Subject.Execute(new ImportListSyncCommand());

            VerifyNotTagged();
        }

        private static bool HasTags(List<Artist> artists, string foreignArtistId, params int[] tags)
        {
            return artists.Single(a => a.ForeignArtistId == foreignArtistId).Tags.SetEquals(tags);
        }
    }
}
