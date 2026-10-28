using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;
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
        private readonly IAlbumService _albumService;

        public RemovedAlbumCheck(ILocalizationService localizationService, IAlbumService albumService)
            : base(localizationService)
        {
            _albumService = albumService;
        }

        public override HealthCheck Check()
        {
            var missingAlbums = _albumService.GetAlbumsMissingFromMetadata();

            if (missingAlbums.Empty())
            {
                return new HealthCheck(GetType());
            }

            var albumText = missingAlbums.Select(a => $"{a.Title} (mbid {a.ForeignAlbumId})").Join(", ");

            if (missingAlbums.Count == 1)
            {
                return new HealthCheck(GetType(),
                    HealthCheckResult.Warning,
                    HealthCheckReason.RemovedAlbumSingle,
                    _localizationService.GetLocalizedString("RemovedAlbumSingleRemovedHealthCheckMessage", new Dictionary<string, object>
                    {
                        { "album", albumText }
                    }));
            }

            return new HealthCheck(GetType(),
                HealthCheckResult.Warning,
                HealthCheckReason.RemovedAlbumMultiple,
                _localizationService.GetLocalizedString("RemovedAlbumMultipleRemovedHealthCheckMessage", new Dictionary<string, object>
                {
                    { "albums", albumText }
                }));
        }
    }
}
