using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Music.ArtistCredits
{
    public class AlbumArtistCredit : ModelBase
    {
        public AlbumArtistCredit()
        {
            Artists = new List<AlbumArtistCreditName>();
        }

        public int AlbumId { get; set; }
        public string Credit { get; set; }
        public string Guests { get; set; }
        public List<AlbumArtistCreditName> Artists { get; set; }
        public DateTime LastFetched { get; set; }

        public static AlbumArtistCredit For(int albumId, List<AlbumArtistCreditName> artists, string primaryForeignArtistId, DateTime fetched)
        {
            return new AlbumArtistCredit
            {
                AlbumId = albumId,
                Artists = artists,
                Credit = Join(artists),
                Guests = Join(artists.Where(a => a.ForeignArtistId != primaryForeignArtistId).ToList()),
                LastFetched = fetched
            };
        }

        // The last name's join phrase is dropped: it only ever links to a name left out of this list.
        private static string Join(List<AlbumArtistCreditName> artists)
        {
            return string.Concat(artists.Select((a, i) => i < artists.Count - 1 ? a.Name + a.JoinPhrase : a.Name));
        }
    }

    public class AlbumArtistCreditName : IEmbeddedDocument
    {
        public string Name { get; set; }
        public string ForeignArtistId { get; set; }
        public string JoinPhrase { get; set; }
    }
}
