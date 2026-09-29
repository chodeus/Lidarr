using System.Collections.Generic;
using System.Linq;
using Lidarr.Http;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Music.ArtistCredits;

namespace Lidarr.Api.V1.Albums
{
    [V1ApiController("album/artistcredit")]
    public class AlbumArtistCreditController : Controller
    {
        private readonly IAlbumArtistCreditService _albumArtistCreditService;

        public AlbumArtistCreditController(IAlbumArtistCreditService albumArtistCreditService)
        {
            _albumArtistCreditService = albumArtistCreditService;
        }

        [HttpGet]
        [Produces("application/json")]
        public List<AlbumArtistCreditResource> GetAlbumArtistCredits([FromQuery] List<int> albumIds)
        {
            return _albumArtistCreditService.GetForAlbums(albumIds).Select(c => c.ToResource()).ToList();
        }
    }
}
