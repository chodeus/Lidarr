using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
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
        private const int ShownAlbums = 5;

        // The wiki has no section for this custom-build check; the branch README says what to do with a kept album
        private static readonly HttpUri KeptAlbumHelp = new HttpUri("https://github.com/chodeus/Lidarr/blob/fix/keep-unlisted-metadata/.github/README.md#what-to-do-with-a-kept-album");

        private readonly IAlbumService _albumService;
        private readonly Logger _logger;
        private string _loggedAlbums;

        public RemovedAlbumCheck(ILocalizationService localizationService, IAlbumService albumService, Logger logger)
            : base(localizationService)
        {
            _albumService = albumService;
            _logger = logger;
        }

        public override HealthCheck Check()
        {
            var missingAlbums = _albumService.GetAlbumsMissingFromMetadata()
                .Select(a => (Name: Name(a), a.ForeignAlbumId))
                .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (missingAlbums.Empty())
            {
                _loggedAlbums = null;
                return new HealthCheck(GetType());
            }

            LogWhenChanged(missingAlbums.Select(a => $"{a.Name} (mbid {a.ForeignAlbumId})").Join(", "));

            if (missingAlbums.Count == 1)
            {
                return new HealthCheck(GetType(),
                    HealthCheckResult.Warning,
                    HealthCheckReason.RemovedAlbumSingle,
                    _localizationService.GetLocalizedString("RemovedAlbumSingleRemovedHealthCheckMessage", new Dictionary<string, object>
                    {
                        { "album", missingAlbums[0].Name }
                    }))
                {
                    WikiUrl = KeptAlbumHelp
                };
            }

            var albumText = missingAlbums.Take(ShownAlbums).Select(a => a.Name).Join(", ");

            if (missingAlbums.Count > ShownAlbums)
            {
                albumText = _localizationService.GetLocalizedString("RemovedAlbumMoreHealthCheckMessage", new Dictionary<string, object>
                {
                    { "albums", albumText },
                    { "count", missingAlbums.Count - ShownAlbums }
                });
            }

            return new HealthCheck(GetType(),
                HealthCheckResult.Warning,
                HealthCheckReason.RemovedAlbumMultiple,
                _localizationService.GetLocalizedString("RemovedAlbumMultipleRemovedHealthCheckMessage", new Dictionary<string, object>
                {
                    { "count", missingAlbums.Count },
                    { "albums", albumText }
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

        // The message shows only the first few, so the whole list goes to the log, once each time it changes
        private void LogWhenChanged(string albums)
        {
            if (albums == _loggedAlbums)
            {
                return;
            }

            _loggedAlbums = albums;
            _logger.Info("Albums kept after the metadata source dropped them: {0}", albums);
        }
    }
}
