using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.ImportListItems;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.ThingiProvider.Events;

namespace NzbDrone.Core.Test.ImportListTests
{
    public class ImportListItemServiceFixture : CoreTest<ImportListItemService>
    {
        private void GivenExisting(List<ImportListItemInfo> existing)
        {
            Mocker.GetMock<IImportListItemRepository>()
                .Setup(v => v.GetAllForLists(It.IsAny<List<int>>()))
                .Returns(existing);
        }

        private static ImportListItemInfo Item(string artistId, string albumId = null)
        {
            return new ImportListItemInfo { Artist = $"Artist {artistId}", ArtistMusicBrainzId = artistId, AlbumMusicBrainzId = albumId };
        }

        [Test]
        public void should_insert_new_update_existing_and_delete_missing()
        {
            GivenExisting(new List<ImportListItemInfo> { Item("6"), Item("7") });

            var newItem = Item("5");
            var updatedItem = Item("6");

            var numDeleted = Subject.SyncItemsForList(new List<ImportListItemInfo> { newItem, updatedItem }, 1);

            numDeleted.Should().Be(1);

            Mocker.GetMock<IImportListItemRepository>()
                .Verify(v => v.InsertMany(It.Is<List<ImportListItemInfo>>(s => s.Count == 1 && s[0].ArtistMusicBrainzId == "5")), Times.Once());

            Mocker.GetMock<IImportListItemRepository>()
                .Verify(v => v.UpdateMany(It.Is<List<ImportListItemInfo>>(s => s.Count == 1 && s[0].ArtistMusicBrainzId == "6")), Times.Once());

            Mocker.GetMock<IImportListItemRepository>()
                .Verify(v => v.DeleteMany(It.Is<List<ImportListItemInfo>>(s => s.Count == 1 && s[0].ArtistMusicBrainzId == "7")), Times.Once());
        }

        [Test]
        public void should_treat_albums_by_the_same_artist_as_separate_items()
        {
            GivenExisting(new List<ImportListItemInfo> { Item("6", "a"), Item("6", "b") });

            var numDeleted = Subject.SyncItemsForList(new List<ImportListItemInfo> { Item("6", "a"), Item("6", "c") }, 1);

            numDeleted.Should().Be(1);

            Mocker.GetMock<IImportListItemRepository>()
                .Verify(v => v.DeleteMany(It.Is<List<ImportListItemInfo>>(s => s.Count == 1 && s[0].AlbumMusicBrainzId == "b")), Times.Once());
        }

        [Test]
        public void should_delete_items_when_list_is_deleted()
        {
            var existing = new List<ImportListItemInfo> { Item("6") };
            GivenExisting(existing);

            Subject.HandleAsync(new ProviderDeletedEvent<IImportList>(3));

            Mocker.GetMock<IImportListItemRepository>()
                .Verify(v => v.GetAllForLists(It.Is<List<int>>(l => l.Count == 1 && l[0] == 3)), Times.Once());

            Mocker.GetMock<IImportListItemRepository>()
                .Verify(v => v.DeleteMany(existing), Times.Once());
        }
    }
}
