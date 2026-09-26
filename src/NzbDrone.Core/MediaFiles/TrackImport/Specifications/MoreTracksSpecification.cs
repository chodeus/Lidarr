using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.Download;
using NzbDrone.Core.History;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.MediaFiles.TrackImport.Specifications
{
    public class MoreTracksSpecification : IImportDecisionEngineSpecification<LocalAlbumRelease>
    {
        private readonly IUpgradableSpecification _upgradableSpecification;
        private readonly ICustomFormatCalculationService _formatService;
        private readonly IHistoryService _historyService;
        private readonly Logger _logger;

        public MoreTracksSpecification(IUpgradableSpecification upgradableSpecification, ICustomFormatCalculationService formatService, IHistoryService historyService, Logger logger)
        {
            _upgradableSpecification = upgradableSpecification;
            _formatService = formatService;
            _historyService = historyService;
            _logger = logger;
        }

        public Decision IsSatisfiedBy(LocalAlbumRelease item, DownloadClientItem downloadClientItem)
        {
            var existingRelease = item.AlbumRelease.Album.Value.AlbumReleases.Value.Single(x => x.Monitored);
            var existingTracks = existingRelease.Tracks.Value.Where(x => x.HasFile).ToList();
            var existingTrackCount = existingTracks.Count;
            if (item.AlbumRelease.Id != existingRelease.Id &&
                item.TrackCount < existingTrackCount)
            {
                if (IsAllowedSmallerReleaseUpgrade(item, existingTracks, downloadClientItem))
                {
                    _logger.Debug($"This release has fewer tracks ({item.TrackCount}) than existing {existingRelease} ({existingTrackCount}) but is a quality upgrade. Accepting {item}");
                    return Decision.Accept();
                }

                _logger.Debug($"This release has fewer tracks ({item.TrackCount}) than existing {existingRelease} ({existingTrackCount}). Skipping {item}");
                return Decision.Reject("Has fewer tracks than existing release");
            }

            _logger.Trace("Accepting release {0}", item);
            return Decision.Accept();
        }

        // Same upgrade test UpgradeDiskSpecification runs at grab, so a release grabbed as an upgrade can import.
        private bool IsAllowedSmallerReleaseUpgrade(LocalAlbumRelease item, List<Track> existingTracks, DownloadClientItem downloadClientItem)
        {
            // Library files joined in by a rescan are the existing files, not an upgrade to them.
            var existingPaths = item.ExistingTracks?.Select(x => x.Path).ToHashSet() ?? new HashSet<string>();
            var incoming = item.LocalTracks.Where(x => !existingPaths.Contains(x.Path)).ToList();
            var existingFiles = existingTracks.Select(x => x.TrackFile?.Value).ToList();
            var artist = incoming.FirstOrDefault()?.Artist;
            var profile = artist?.QualityProfile?.Value;

            if (profile?.AllowSmallerReleaseUpgrades != true || incoming.Any(x => x.Quality == null) || existingFiles.Any(x => x?.Quality == null))
            {
                return false;
            }

            var comparer = new QualityModelComparer(profile);
            var worstQuality = incoming.Select(x => x.Quality).OrderBy(x => x, comparer).First();
            var worstFormats = IncomingFormats(incoming, artist, profile, downloadClientItem);

            return _upgradableSpecification.IsUpgradable(profile,
                                                         existingFiles.Select(x => x.Quality).Distinct().ToList(),
                                                         _formatService.ParseCustomFormat(existingFiles[0], artist),
                                                         worstQuality,
                                                         worstFormats);
        }

        // An album download's files don't carry its release title, so score what was grabbed.
        private List<CustomFormat> IncomingFormats(List<LocalTrack> incoming, Artist artist, QualityProfile profile, DownloadClientItem downloadClientItem)
        {
            var grab = downloadClientItem?.DownloadId.IsNotNullOrWhiteSpace() == true
                ? _historyService.Find(downloadClientItem.DownloadId, EntityHistoryEventType.Grabbed).MaxBy(x => x.Date)
                : null;

            if (grab != null)
            {
                return _formatService.ParseCustomFormat(grab, artist);
            }

            return incoming.Select(x => _formatService.ParseCustomFormat(x))
                           .OrderBy(x => profile.CalculateCustomFormatScore(x))
                           .First();
        }
    }
}
