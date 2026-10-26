using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.ImportLists
{
    // The artists a set of list items names: by ID, a former ID, clean name, or the artist of a listed album in the library
    public class ListedArtists
    {
        private readonly HashSet<string> _artistIds;
        private readonly HashSet<string> _artistNames;
        private readonly HashSet<int> _albumArtistMetadataIds;

        public ListedArtists(IEnumerable<ImportListItemInfo> items, ILookup<string, Album> listedAlbums)
        {
            var list = items.ToList();

            _artistIds = list.SelectMany(l => new[] { l.ArtistMusicBrainzId, l.MappedArtistMusicBrainzId }).Where(x => x.IsNotNullOrWhiteSpace()).ToHashSet();
            _artistNames = list.Where(l => l.Artist.IsNotNullOrWhiteSpace()).Select(l => l.Artist.CleanArtistName()).ToHashSet();
            _albumArtistMetadataIds = list.Where(l => l.AlbumMusicBrainzId.IsNotNullOrWhiteSpace())
                                          .SelectMany(l => listedAlbums[l.AlbumMusicBrainzId])
                                          .Select(a => a.ArtistMetadataId)
                                          .ToHashSet();
        }

        public bool Contains(Artist artist)
        {
            return _artistIds.Contains(artist.ForeignArtistId) ||
                   artist.Metadata.Value.OldForeignArtistIds.Any(_artistIds.Contains) ||
                   _artistNames.Contains(artist.CleanName) ||
                   _albumArtistMetadataIds.Contains(artist.ArtistMetadataId);
        }

        // Tied to no artist by ID or by an album in the library, so only its name could match its artist
        public static bool IsUnresolved(ImportListItemInfo item, ILookup<string, Album> listedAlbums)
        {
            return item.ArtistMusicBrainzId.IsNullOrWhiteSpace() &&
                   item.MappedArtistMusicBrainzId.IsNullOrWhiteSpace() &&
                   (item.AlbumMusicBrainzId.IsNullOrWhiteSpace() || !listedAlbums.Contains(item.AlbumMusicBrainzId));
        }
    }
}
