using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Music.ArtistCredits;

namespace Lidarr.Api.V1.Albums
{
    public class AlbumArtistCreditResource
    {
        public int AlbumId { get; set; }
        public string Credit { get; set; }
        public string Guests { get; set; }
        public List<AlbumArtistCreditNameResource> Artists { get; set; }
        public DateTime LastFetched { get; set; }
    }

    public class AlbumArtistCreditNameResource
    {
        public string Name { get; set; }
        public string ForeignArtistId { get; set; }
        public string JoinPhrase { get; set; }
    }

    public static class AlbumArtistCreditResourceMapper
    {
        public static AlbumArtistCreditResource ToResource(this AlbumArtistCredit model)
        {
            return new AlbumArtistCreditResource
            {
                AlbumId = model.AlbumId,
                Credit = model.Credit,
                Guests = model.Guests,
                Artists = model.Artists.Select(a => new AlbumArtistCreditNameResource
                {
                    Name = a.Name,
                    ForeignArtistId = a.ForeignArtistId,
                    JoinPhrase = a.JoinPhrase
                }).ToList(),
                LastFetched = model.LastFetched
            };
        }
    }
}
