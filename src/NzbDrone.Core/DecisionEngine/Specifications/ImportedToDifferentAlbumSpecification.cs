using System;
using System.Linq;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.History;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.DecisionEngine.Specifications
{
    public class ImportedToDifferentAlbumSpecification : IDecisionEngineSpecification
    {
        // Long enough to stop a daily re-grab loop, short enough to retry once the metadata has been corrected
        private static readonly TimeSpan GrabHistoryWindow = TimeSpan.FromDays(14);

        private readonly IHistoryService _historyService;
        private readonly IConfigService _configService;
        private readonly Logger _logger;

        public ImportedToDifferentAlbumSpecification(IHistoryService historyService,
                                                     IConfigService configService,
                                                     Logger logger)
        {
            _historyService = historyService;
            _configService = configService;
            _logger = logger;
        }

        public SpecificationPriority Priority => SpecificationPriority.Database;
        public RejectionType Type => RejectionType.Permanent;

        public Decision IsSatisfiedBy(RemoteAlbum subject, SearchCriteriaBase searchCriteria)
        {
            if (!_configService.EnableCompletedDownloadHandling)
            {
                _logger.Debug("Skipping imported to different album check because CDH is disabled");
                return Decision.Accept();
            }

            _logger.Debug("Performing imported to different album check on report");

            var grabbedSince = DateTime.UtcNow - GrabHistoryWindow;

            foreach (var album in subject.Albums)
            {
                // History is returned newest first
                var lastGrab = _historyService.GetByAlbum(album.Id, EntityHistoryEventType.Grabbed)
                                              .FirstOrDefault(h => h.Date >= grabbedSince && IsSameRelease(subject.Release, h));

                if (lastGrab?.DownloadId == null)
                {
                    continue;
                }

                var downloadHistory = _historyService.FindByDownloadId(lastGrab.DownloadId);

                // Only track file events record where each file went, the download event records the parsed albums.
                // A re-grabbed torrent keeps its info hash, so earlier attempts share this download id.
                var importedAlbumIds = downloadHistory.Where(h => h.EventType == EntityHistoryEventType.TrackFileImported && h.Date > lastGrab.Date)
                                                      .Select(h => h.AlbumId)
                                                      .ToHashSet();

                if (importedAlbumIds.Count == 0)
                {
                    continue;
                }

                var grabbedAlbumIds = downloadHistory.Where(h => h.EventType == EntityHistoryEventType.Grabbed)
                                                     .Select(h => h.AlbumId);

                if (importedAlbumIds.Overlaps(grabbedAlbumIds))
                {
                    continue;
                }

                _logger.Debug("Release grabbed for album {0} on {1} was imported into album(s) {2}", album.Id, lastGrab.Date, string.Join(", ", importedAlbumIds));
                return Decision.Reject("Last grab of this release was imported into a different album");
            }

            return Decision.Accept();
        }

        private static bool IsSameRelease(ReleaseInfo release, EntityHistory grab)
        {
            if (release.DownloadProtocol == nameof(TorrentDownloadProtocol) &&
                release is TorrentInfo torrentInfo &&
                torrentInfo.InfoHash != null &&
                torrentInfo.InfoHash.Equals(grab.DownloadId, StringComparison.InvariantCultureIgnoreCase))
            {
                return true;
            }

            // Same reasoning as AlreadyImportedSpecification: a release with the same title very likely has the same content
            return release.Title.Equals(grab.SourceTitle, StringComparison.InvariantCultureIgnoreCase);
        }
    }
}
