using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Music;
using NzbDrone.Core.Music.Events;

namespace NzbDrone.Core.HealthCheck.Checks
{
    [CheckOn(typeof(AlbumUpdatedEvent))]
    [CheckOn(typeof(AlbumDeletedEvent), CheckOnCondition.FailedOnly)]
    [CheckOn(typeof(ArtistsDeletedEvent), CheckOnCondition.FailedOnly)]
    public class RemovedAlbumCheck : HealthCheckBase
    {
        // The wiki has no section for this custom-build check; the branch README says what to do with these albums
        private static readonly HttpUri KeptAlbumHelp = new HttpUri("https://github.com/chodeus/Lidarr/blob/fix/keep-unlisted-metadata/.github/README.md#what-to-do-with-an-album-in-the-warning");

        private readonly IAlbumService _albumService;

        public RemovedAlbumCheck(ILocalizationService localizationService, IAlbumService albumService)
            : base(localizationService)
        {
            _albumService = albumService;
        }

        public override HealthCheck Check()
        {
            // A kept album with files needs nothing done, so only the ones without files are listed
            var albums = _albumService.GetAlbumsMissingFromMetadataWithoutFiles()
                .Select(Name)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (albums.Empty())
            {
                return new HealthCheck(GetType());
            }

            if (albums.Count == 1)
            {
                return new HealthCheck(GetType(),
                    HealthCheckResult.Warning,
                    HealthCheckReason.RemovedAlbumSingle,
                    _localizationService.GetLocalizedString("RemovedAlbumSingleRemovedHealthCheckMessage", new Dictionary<string, object>
                    {
                        { "album", albums[0] }
                    }))
                {
                    WikiUrl = KeptAlbumHelp
                };
            }

            return new HealthCheck(GetType(),
                HealthCheckResult.Warning,
                HealthCheckReason.RemovedAlbumMultiple,
                _localizationService.GetLocalizedString("RemovedAlbumMultipleRemovedHealthCheckMessage", new Dictionary<string, object>
                {
                    { "count", albums.Count },
                    { "albums", albums.Join(", ") }
                }))
            {
                WikiUrl = KeptAlbumHelp
            };
        }

        private static string Name(Album album)
        {
            var artist = album.ArtistMetadata?.Value?.Name;

            return artist.IsNullOrWhiteSpace() ? album.Title : $"{artist} - {album.Title}";
        }
    }
}
