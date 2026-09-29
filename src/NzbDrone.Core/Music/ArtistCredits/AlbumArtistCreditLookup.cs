using System;

namespace NzbDrone.Core.Music.ArtistCredits
{
    // Static so the tag writer and naming need no new constructor parameters: other branches of this build
    // change those constructors, and a changed signature would break the merge. AlbumArtistCreditService sets it.
    public static class AlbumArtistCreditLookup
    {
        // Swapped as one object so a reader never pairs a new find with an old option.
        private static Source _source = new Source(_ => null, () => false);

        public static void Configure(Func<int, AlbumArtistCredit> find, Func<bool> writeToTags)
        {
            _source = new Source(find, writeToTags);
        }

        public static void Reset()
        {
            Configure(_ => null, () => false);
        }

        /// <summary>The credited names besides the album's primary artist, or empty.</summary>
        public static string Guests(Album album)
        {
            return album?.Id > 0 ? _source.Find(album.Id)?.Guests ?? string.Empty : string.Empty;
        }

        /// <summary>The album artist tag: the full credit when that option is on and a credit is stored.</summary>
        public static string AlbumArtistTag(Album album, string primaryName)
        {
            var source = _source;
            if (!source.WriteToTags() || !(album?.Id > 0))
            {
                return primaryName;
            }

            var credit = source.Find(album.Id)?.Credit;

            return string.IsNullOrWhiteSpace(credit) ? primaryName : credit;
        }

        /// <summary>The track artist tag: the album artist tag only for tracks by the album's own artist, so a guest's track keeps its artist.</summary>
        public static string TrackArtistTag(string albumArtistTag, ArtistMetadata trackArtist, int albumArtistMetadataId)
        {
            return trackArtist.Id == albumArtistMetadataId ? albumArtistTag : trackArtist.Name;
        }

        private class Source
        {
            public Source(Func<int, AlbumArtistCredit> find, Func<bool> writeToTags)
            {
                Find = find;
                WriteToTags = writeToTags;
            }

            public Func<int, AlbumArtistCredit> Find { get; }
            public Func<bool> WriteToTags { get; }
        }
    }
}
