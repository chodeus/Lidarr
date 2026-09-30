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
            var unmatched = existingListItems.GroupBy(Key).ToDictionary(g => g.Key, g => new Queue<ImportListItemInfo>(g));

            var toAdd = new List<ImportListItemInfo>();
            var toUpdate = new List<ImportListItemInfo>();

            listItems.ForEach(item =>
            {
                if (!unmatched.TryGetValue(Key(item), out var matches) || !matches.TryDequeue(out var existingItem))
                {
                    toAdd.Add(item);
                    return;
                }

                if (existingItem.ReleaseDate != item.ReleaseDate)
                {
                    existingItem.ReleaseDate = item.ReleaseDate;
                    toUpdate.Add(existingItem);
                }
            });

            var toDelete = unmatched.Values.SelectMany(q => q).ToList();

            _importListItemRepository.InsertMany(toAdd);
            _importListItemRepository.SetFields(toUpdate, i => i.ReleaseDate);
            _importListItemRepository.DeleteMany(toDelete);

            return toDelete.Count;
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
        private static (string Artist, string ArtistMusicBrainzId, string Album, string AlbumMusicBrainzId) Key(ImportListItemInfo item)
        {
            return (item.Artist, item.ArtistMusicBrainzId, item.Album, item.AlbumMusicBrainzId);
        }
    }
}
