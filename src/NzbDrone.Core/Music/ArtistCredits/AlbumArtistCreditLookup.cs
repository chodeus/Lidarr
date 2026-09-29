using System;

namespace NzbDrone.Core.Music.ArtistCredits
{
    // Static so the tag writer and naming need no new constructor parameters: other branches of this build
    // change those constructors, and a changed signature would break the merge. AlbumArtistCreditService sets it.
    public static class AlbumArtistCreditLookup
    {
        private static Func<int, AlbumArtistCredit> _find = _ => null;
        private static Func<bool> _writeToTags = () => false;

        public static void Configure(Func<int, AlbumArtistCredit> find, Func<bool> writeToTags)
        {
            _find = find;
            _writeToTags = writeToTags;
        }

        public static void Reset()
        {
            Configure(_ => null, () => false);
        }

        /// <summary>The credited names besides the album's primary artist, or empty.</summary>
        public static string Guests(Album album)
        {
            return album?.Id > 0 ? _find(album.Id)?.Guests ?? string.Empty : string.Empty;
        }

        /// <summary>The album artist tag: the full credit when that option is on and a credit is stored.</summary>
        public static string AlbumArtistTag(Album album, string primaryName)
        {
            if (!_writeToTags() || !(album?.Id > 0))
            {
                return primaryName;
            }

            var credit = _find(album.Id)?.Credit;

            return string.IsNullOrWhiteSpace(credit) ? primaryName : credit;
        }

        /// <summary>The track artist tag: the album credit only for tracks by the album's own artist, so a guest's track keeps its artist.</summary>
        public static string TrackArtistTag(Album album, ArtistMetadata trackArtist, int albumArtistMetadataId)
        {
            return trackArtist.Id == albumArtistMetadataId ? AlbumArtistTag(album, trackArtist.Name) : trackArtist.Name;
        }
    }
}
