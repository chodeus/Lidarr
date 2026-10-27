using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NzbDrone.Core.Music;

namespace NzbDrone.Core.MetadataSource.Sources
{
    // Which of another source's albums the artist doesn't have yet
    public static class MissingAlbums
    {
        private static readonly Regex EditionSuffix = new Regex(@"\s*[\(\[][^\)\]]*\b(?:deluxe|remaster(?:ed)?|edition|expanded|anniversary|bonus|special|collector'?s)\b[^\)\]]*[\)\]]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // An album the artist has, or had before a merge, can come back under another title
        public static List<Album> ById(IEnumerable<Album> known, IEnumerable<Album> candidates)
        {
            var owners = new Dictionary<string, Album>();

            foreach (var album in known)
            {
                Own(owners, album, album);
            }

            var missing = new List<Album>();

            foreach (var album in candidates)
            {
                var match = IdsOf(album).Select(id => owners.GetValueOrDefault(id)).FirstOrDefault(a => a != null);

                if (match == null)
                {
                    missing.Add(album);
                    Own(owners, album, album);
                    continue;
                }

                // Like an adopted album: its IDs become old IDs of the match, so a later source listing it by one of them matches too
                match.OldForeignAlbumIds ??= new List<string>();
                match.OldForeignAlbumIds.AddRange(IdsOf(album).Where(id => id != match.ForeignAlbumId && !match.OldForeignAlbumIds.Contains(id)).Distinct().ToList());
                Own(owners, album, match);
            }

            return missing;
        }

        // For a source whose IDs needn't match the artist's, where the same title is the same album
        public static List<Album> ByIdOrTitle(IEnumerable<Album> known, IEnumerable<Album> candidates)
        {
            var knownAlbums = known.ToList();
            var candidateList = candidates.ToList();
            var missing = ById(knownAlbums, candidateList);

            // A known album the source lists by ID is that one, so the source's other same-title albums are other albums
            var listedIds = candidateList.SelectMany(IdsOf).ToHashSet();
            var keys = knownAlbums.Select(k => (Key: Key(k), ListedById: IdsOf(k).Any(listedIds.Contains))).ToList();

            // Only against the known albums: one source's own same-title albums are different albums
            return missing.Where(album =>
            {
                var key = Key(album);

                return !keys.Any(k => k.ListedById ? EditionOf(k.Key, key) : SameAlbum(k.Key, key));
            }).ToList();
        }

        // The known album each candidate is by the title rule: one same-title candidate per known album, the nearest in year, and any edition
        public static List<(Album Album, Album Match)> Matches(IEnumerable<Album> known, IEnumerable<Album> candidates)
        {
            var knownKeys = known.Select(k => (Album: k, Key: Key(k))).ToList();
            var pairs = candidates.SelectMany(album => Pairs(knownKeys, album)).OrderBy(p => p.Edition).ThenBy(p => p.Gap).ToList();
            var matched = Albums();
            var taken = Albums();
            var matches = new List<(Album Album, Album Match)>();

            foreach (var pair in pairs)
            {
                if (matched.Contains(pair.Album) || (!pair.Edition && !taken.Add(pair.Match)))
                {
                    continue;
                }

                matched.Add(pair.Album);
                matches.Add((pair.Album, pair.Match));
            }

            return matches;
        }

        private static IEnumerable<(Album Album, Album Match, bool Edition, int Gap)> Pairs(List<(Album Album, AlbumKey Key)> known, Album album)
        {
            var key = Key(album);

            return known.Where(k => SameAlbum(k.Key, key))
                        .Select(k => k.Key.Title == key.Title ? (album, k.Album, false, Math.Abs(k.Key.Year.Value - key.Year.Value)) : (album, k.Album, true, 0));
        }

        // By reference: albums compare by value, so two equal albums would count as one
        private static HashSet<Album> Albums()
        {
            return new HashSet<Album>(ReferenceEqualityComparer.Instance);
        }

        private static void Own(Dictionary<string, Album> owners, Album album, Album owner)
        {
            foreach (var id in IdsOf(album))
            {
                owners.TryAdd(id, owner);
            }
        }

        private static IEnumerable<string> IdsOf(Album album)
        {
            return (album.OldForeignAlbumIds ?? new List<string>()).Append(album.ForeignAlbumId);
        }

        // The same title within a year (both known), or an edition of the same title from any year
        private static bool SameAlbum(AlbumKey a, AlbumKey b)
        {
            if (a.Title == b.Title)
            {
                return a.Year != null && b.Year != null && Math.Abs(a.Year.Value - b.Year.Value) <= 1;
            }

            return EditionOf(a, b);
        }

        private static bool EditionOf(AlbumKey a, AlbumKey b)
        {
            return a.Title != b.Title && (a.IsEdition || b.IsEdition) && a.BaseTitle == b.BaseTitle;
        }

        private static AlbumKey Key(Album album)
        {
            var title = album.Title ?? string.Empty;
            var baseTitle = EditionSuffix.Replace(title, string.Empty);

            return new AlbumKey(Clean(title), Clean(baseTitle), album.ReleaseDate?.Year, baseTitle.Length != title.Length);
        }

        // A title of symbols only cleans to nothing, so it is compared as written
        private static string Clean(string title)
        {
            var clean = Parser.Parser.CleanArtistName(title);

            return string.IsNullOrWhiteSpace(clean) ? title.Trim().ToLowerInvariant() : clean;
        }

        private record AlbumKey(string Title, string BaseTitle, int? Year, bool IsEdition);
    }
}
