using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NzbDrone.Core.Music;

namespace NzbDrone.Core.MetadataSource.Sources
{
    // Which of a supplementary source's albums the artist doesn't have yet
    public static class SupplementaryAlbums
    {
        private static readonly Regex EditionSuffix = new Regex(@"\s*[\(\[][^\)\]]*\b(?:deluxe|remaster(?:ed)?|edition|expanded|anniversary|bonus|special|collector'?s)\b[^\)\]]*[\)\]]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static List<Album> Missing(IEnumerable<Album> known, IEnumerable<Album> candidates)
        {
            var knownAlbums = known.ToList();
            var keys = knownAlbums.Select(Key).ToList();
            var ids = knownAlbums.SelectMany(IdsOf).ToHashSet();
            var missing = new List<Album>();

            foreach (var album in candidates)
            {
                var key = Key(album);

                // A source with the same kind of IDs can list an album the artist has, or had before a merge, under another title
                if (IdsOf(album).Any(ids.Contains) || keys.Any(k => SameAlbum(k, key)))
                {
                    continue;
                }

                keys.Add(key);
                ids.UnionWith(IdsOf(album));
                missing.Add(album);
            }

            return missing;
        }

        private static IEnumerable<string> IdsOf(Album album)
        {
            return (album.OldForeignAlbumIds ?? new List<string>()).Append(album.ForeignAlbumId);
        }

        // The same title within a year, or an edition of the same title from any year
        private static bool SameAlbum(AlbumKey a, AlbumKey b)
        {
            if (a.Title == b.Title)
            {
                return a.Year == null || b.Year == null || Math.Abs(a.Year.Value - b.Year.Value) <= 1;
            }

            return (a.IsEdition || b.IsEdition) && a.BaseTitle == b.BaseTitle;
        }

        private static AlbumKey Key(Album album)
        {
            var title = album.Title ?? string.Empty;
            var baseTitle = EditionSuffix.Replace(title, string.Empty);

            return new AlbumKey(Parser.Parser.CleanArtistName(title), Parser.Parser.CleanArtistName(baseTitle), album.ReleaseDate?.Year, baseTitle.Length != title.Length);
        }

        private record AlbumKey(string Title, string BaseTitle, int? Year, bool IsEdition);
    }
}
