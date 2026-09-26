using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.MediaFiles.TrackImport.Specifications
{
    public class MoreTracksSpecification : IImportDecisionEngineSpecification<LocalAlbumRelease>
    {
        private readonly Logger _logger;

        public MoreTracksSpecification(Logger logger)
        {
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
                if (IsAllowedSmallerReleaseUpgrade(item, existingTracks))
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

        // A whole release replaces a bigger one only when the profile allows it, no file gets worse and at least one gets better.
        private static bool IsAllowedSmallerReleaseUpgrade(LocalAlbumRelease item, List<Track> existingTracks)
        {
            // Library files joined in by a rescan are the existing files, not an upgrade to them.
            var existingPaths = item.ExistingTracks?.Select(x => x.Path).ToHashSet() ?? new HashSet<string>();
            var incoming = item.LocalTracks.Where(x => !existingPaths.Contains(x.Path)).ToList();
            var existingQualities = existingTracks.Select(x => x.TrackFile?.Value?.Quality).ToList();
            var profile = incoming.FirstOrDefault()?.Artist?.QualityProfile?.Value;

            if (profile?.AllowSmallerReleaseUpgrades != true || incoming.Any(x => x.Quality == null) || existingQualities.Any(x => x == null))
            {
                return false;
            }

            var comparer = new QualityModelComparer(profile);
            var worstIncoming = incoming.Select(x => x.Quality).OrderBy(x => x, comparer).First();

            return existingQualities.All(x => comparer.Compare(worstIncoming, x) >= 0) &&
                   existingQualities.Any(x => comparer.Compare(worstIncoming, x) > 0);
        }
    }
}
