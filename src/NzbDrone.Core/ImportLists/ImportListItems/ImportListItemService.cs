using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.ThingiProvider.Events;

namespace NzbDrone.Core.ImportLists.ImportListItems
{
    public interface IImportListItemService
    {
        List<ImportListItemInfo> All();
        List<ImportListItemInfo> GetAllForLists(List<int> listIds);
        int SyncItemsForList(List<ImportListItemInfo> listItems, int listId);
    }

    public class ImportListItemService : IImportListItemService, IHandleAsync<ProviderDeletedEvent<IImportList>>
    {
        private readonly IImportListItemRepository _importListItemRepository;

        public ImportListItemService(IImportListItemRepository importListItemRepository)
        {
            _importListItemRepository = importListItemRepository;
        }

        public int SyncItemsForList(List<ImportListItemInfo> listItems, int listId)
        {
            var existingListItems = GetAllForLists(new List<int> { listId });

            var toAdd = new List<ImportListItemInfo>();
            var toUpdate = new List<ImportListItemInfo>();

            listItems.ForEach(item =>
            {
                var existingItem = FindItem(existingListItems, item);

                if (existingItem == null)
                {
                    toAdd.Add(item);
                    return;
                }

                // Remove so we'll only be left with items to remove at the end
                existingListItems.Remove(existingItem);
                toUpdate.Add(existingItem);

                existingItem.ReleaseDate = item.ReleaseDate;
            });

            _importListItemRepository.InsertMany(toAdd);
            _importListItemRepository.UpdateMany(toUpdate);
            _importListItemRepository.DeleteMany(existingListItems);

            return existingListItems.Count;
        }

        public List<ImportListItemInfo> All()
        {
            return _importListItemRepository.All().ToList();
        }

        public List<ImportListItemInfo> GetAllForLists(List<int> listIds)
        {
            return _importListItemRepository.GetAllForLists(listIds).ToList();
        }

        public void HandleAsync(ProviderDeletedEvent<IImportList> message)
        {
            var itemsOnList = _importListItemRepository.GetAllForLists(new List<int> { message.ProviderId });
            _importListItemRepository.DeleteMany(itemsOnList);
        }

        // Same identity as the fetch's DistinctBy
        private ImportListItemInfo FindItem(List<ImportListItemInfo> existingItems, ImportListItemInfo item)
        {
            return existingItems.FirstOrDefault(e =>
                e.Artist == item.Artist &&
                e.ArtistMusicBrainzId == item.ArtistMusicBrainzId &&
                e.Album == item.Album &&
                e.AlbumMusicBrainzId == item.AlbumMusicBrainzId);
        }
    }
}
