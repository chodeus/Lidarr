using System;
using System.Collections.Generic;
using NzbDrone.Core.Music;

namespace NzbDrone.Core.Notifications
{
    public static class NotificationMetadataLinkGenerator
    {
        public static List<NotificationMetadataLink> GenerateLinks(Artist artist, List<Album> albums, IEnumerable<int> metadataLinks)
        {
            var links = new List<NotificationMetadataLink>();

            if (artist == null)
            {
                return links;
            }

            var artistId = artist.Metadata?.Value?.ForeignArtistId;

            foreach (var link in metadataLinks)
            {
                var linkType = (MetadataLinkType)link;

                if (linkType == MetadataLinkType.MusicBrainzArtist && IsMusicBrainzId(artistId))
                {
                    links.Add(new NotificationMetadataLink(MetadataLinkType.MusicBrainzArtist, "MusicBrainz Artist", $"https://musicbrainz.org/artist/{artistId}"));
                }

                // Only a message about one album has an album page to link
                if (linkType == MetadataLinkType.MusicBrainzAlbum && albums is { Count: 1 } && IsMusicBrainzId(albums[0].ForeignAlbumId))
                {
                    links.Add(new NotificationMetadataLink(MetadataLinkType.MusicBrainzAlbum, "MusicBrainz Album", $"https://musicbrainz.org/release-group/{albums[0].ForeignAlbumId}"));
                }
            }

            return links;
        }

        // Other metadata sources' IDs have no MusicBrainz page
        private static bool IsMusicBrainzId(string id)
        {
            return Guid.TryParseExact(id, "D", out _);
        }
    }
}
