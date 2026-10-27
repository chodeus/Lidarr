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
            var keys = knownAlbums.Select(Key).ToList();

            // Only against the known albums: one source's own same-title albums are different albums
            return ById(knownAlbums, candidates).Where(album => !keys.Any(k => SameAlbum(k, Key(album)))).ToList();
        }

        // The known album that is this one by the title rule, if any
        public static Album SameAlbumAs(IEnumerable<Album> known, Album album)
        {
            var key = Key(album);

            return known.FirstOrDefault(k => SameAlbum(Key(k), key));
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

            return (a.IsEdition || b.IsEdition) && a.BaseTitle == b.BaseTitle;
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
