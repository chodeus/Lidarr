using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;
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
        void StoreMappedArtistIds(List<(ImportListItemKey Key, string ArtistMusicBrainzId)> mapped);
    }

    public record ImportListItemKey(string Artist, string ArtistMusicBrainzId, string Album, string AlbumMusicBrainzId);

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
            var unmatched = existingListItems.GroupBy(KeyOf).ToDictionary(g => g.Key, g => new Queue<ImportListItemInfo>(g));

            var toAdd = new List<ImportListItemInfo>();
            var toUpdate = new List<ImportListItemInfo>();

            listItems.ForEach(item =>
            {
                item.ReleaseDate = AsUtc(item.ReleaseDate);

                if (!unmatched.TryGetValue(KeyOf(item), out var matches) || !matches.TryDequeue(out var existingItem))
                {
                    toAdd.Add(item);
                    return;
                }

                if (AsUtc(existingItem.ReleaseDate) != item.ReleaseDate)
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

        public void StoreMappedArtistIds(List<(ImportListItemKey Key, string ArtistMusicBrainzId)> mapped)
        {
            var byKey = mapped.Where(m => m.Key.ArtistMusicBrainzId.IsNullOrWhiteSpace() && m.ArtistMusicBrainzId.IsNotNullOrWhiteSpace())
                              .GroupBy(m => m.Key)
                              .ToDictionary(g => g.Key, g => g.First().ArtistMusicBrainzId);

            var toUpdate = _importListItemRepository.All()
                .Where(i => byKey.TryGetValue(KeyOf(i), out var artistId) && i.MappedArtistMusicBrainzId != artistId)
                .ToList();

            toUpdate.ForEach(i => i.MappedArtistMusicBrainzId = byKey[KeyOf(i)]);

            _importListItemRepository.SetFields(toUpdate, i => i.MappedArtistMusicBrainzId);
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

        // The fetch's DistinctBy identity, ignoring case, so a provider changing a name's case isn't a removal
        public static ImportListItemKey KeyOf(ImportListItemInfo item)
        {
            return new ImportListItemKey(item.Artist?.ToLowerInvariant(), item.ArtistMusicBrainzId?.ToLowerInvariant(), item.Album?.ToLowerInvariant(), item.AlbumMusicBrainzId?.ToLowerInvariant());
        }

        // Lists give dates without a zone, which the database would store shifted by the host's offset
        private static DateTime AsUtc(DateTime date)
        {
            return date.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(date, DateTimeKind.Utc) : date.ToUniversalTime();
        }
    }
}
