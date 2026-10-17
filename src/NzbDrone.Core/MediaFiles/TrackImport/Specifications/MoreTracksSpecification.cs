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

        // Develop's constructor, kept for plugins built against it; without the services a smaller release is rejected, as in develop
        public MoreTracksSpecification(Logger logger)
            : this(null, null, null, logger)
        {
        }

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

            if (_upgradableSpecification == null || profile?.UpgradeAllowed != true || !profile.AllowSmallerReleaseUpgrades || incoming.Any(x => x.Quality == null) || existingFiles.Any(x => x?.Quality == null))
            {
                return false;
            }

            var comparer = new QualityModelComparer(profile);
            var worstQuality = incoming.Select(x => x.Quality).OrderBy(x => x, comparer).First();
            var (existingFormats, incomingFormats) = ScoredFormats(item, incoming, existingTracks, existingFiles, artist, profile, downloadClientItem);

            return _upgradableSpecification.IsUpgradable(profile,
                                                         existingFiles.Select(x => x.Quality).Distinct().ToList(),
                                                         existingFormats,
                                                         worstQuality,
                                                         incomingFormats);
        }

        // Both sides need the same kind of title, or a format only one of them shows fakes an upgrade.
        // An album download's files don't carry its release title, so grabs are scored when both sides have them.
        private (List<CustomFormat> Existing, List<CustomFormat> Incoming) ScoredFormats(LocalAlbumRelease item, List<LocalTrack> incoming, List<Track> existingTracks, List<TrackFile> existingFiles, Artist artist, QualityProfile profile, DownloadClientItem downloadClientItem)
        {
            var incomingGrab = FindGrab(downloadClientItem?.DownloadId);
            var existingGrabs = incomingGrab == null ? null : ExistingGrabs(item.AlbumRelease.Album.Value.Id, existingTracks);

            if (existingGrabs != null)
            {
                return (BestScored(existingGrabs.Select(x => _formatService.ParseCustomFormat(x, artist)), profile),
                        _formatService.ParseCustomFormat(incomingGrab, artist));
            }

            // The worst new file against the best file on disk
            return (BestScored(existingFiles.Select(x => _formatService.ParseCustomFormat(x, artist)), profile),
                    incoming.Select(x => _formatService.ParseCustomFormat(x)).OrderBy(x => profile.CalculateCustomFormatScore(x)).First());
        }

        private EntityHistory FindGrab(string downloadId)
        {
            return downloadId.IsNotNullOrWhiteSpace()
                ? _historyService.Find(downloadId, EntityHistoryEventType.Grabbed).MaxBy(x => x.Date)
                : null;
        }

        // The grab behind each file on disk, via the latest import of its track; null when any file has none.
        private List<EntityHistory> ExistingGrabs(int albumId, List<Track> existingTracks)
        {
            var imports = _historyService.GetByAlbum(albumId, EntityHistoryEventType.TrackFileImported);
            var grabs = new List<EntityHistory>();

            foreach (var downloadId in existingTracks.Select(t => ImportedDownloadId(t, imports)).Distinct())
            {
                var grab = FindGrab(downloadId);

                if (grab == null)
                {
                    return null;
                }

                grabs.Add(grab);
            }

            return grabs;
        }

        // A file a rescan added after the track's last import didn't come from that import's grab
        private static string ImportedDownloadId(Track track, List<EntityHistory> imports)
        {
            var import = imports.Where(x => x.TrackId == track.Id).MaxBy(x => x.Date);
            var file = track.TrackFile?.Value;

            return import != null && file != null && import.Date.AddMinutes(1) >= file.DateAdded ? import.DownloadId : null;
        }

        private static List<CustomFormat> BestScored(IEnumerable<List<CustomFormat>> candidates, QualityProfile profile)
        {
            return candidates.OrderByDescending(x => profile.CalculateCustomFormatScore(x)).First();
        }
    }
}
