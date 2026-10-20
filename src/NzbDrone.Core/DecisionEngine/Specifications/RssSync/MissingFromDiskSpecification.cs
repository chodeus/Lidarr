using System.Linq;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.DecisionEngine.Specifications.RssSync
{
    // DeletedTrackFileSpecification holds back albums whose files are in the database but not on disk. Once a scan drops
    // them from the database, this holds the albums back until the scan decides whether to unmonitor them.
    public class MissingFromDiskSpecification : IDecisionEngineSpecification
    {
        private readonly IConfigService _configService;
        private readonly IAlbumService _albumService;
        private readonly IUnmonitorDeletedAlbumsService _unmonitorDeletedAlbumsService;
        private readonly Logger _logger;

        public MissingFromDiskSpecification(IConfigService configService,
                                            IAlbumService albumService,
                                            IUnmonitorDeletedAlbumsService unmonitorDeletedAlbumsService,
                                            Logger logger)
        {
            _configService = configService;
            _albumService = albumService;
            _unmonitorDeletedAlbumsService = unmonitorDeletedAlbumsService;
            _logger = logger;
        }

        public SpecificationPriority Priority => SpecificationPriority.Disk;
        public RejectionType Type => RejectionType.Temporary;

        public virtual Decision IsSatisfiedBy(RemoteAlbum subject, SearchCriteriaBase searchCriteria)
        {
            if (!_configService.AutoUnmonitorPreviouslyDownloadedTracks)
            {
                return Decision.Accept();
            }

            if (searchCriteria != null)
            {
                _logger.Debug("Skipping missing from disk check during search");
                return Decision.Accept();
            }

            // A scan has already dropped these files from the database, and has yet to decide whether to unmonitor the album.
            if (subject.Albums.Any(v => _unmonitorDeletedAlbumsService.IsMissingFromDisk(v.Id)))
            {
                _logger.Debug("Files for this album are missing from disk, waiting for a disk scan of their root folder to decide whether to unmonitor it. skipping.");
                return Decision.Reject("Album files are missing from disk");
            }

            // The albums were loaded when the release was matched. A scan may have unmonitored one since and only then stopped
            // holding it back, so this comes after the hold.
            if (_albumService.GetAlbums(subject.Albums.Where(v => v.Monitored).Select(v => v.Id).ToList()).Any(v => !v.Monitored))
            {
                _logger.Debug("Album was unmonitored after the release was matched to it. skipping.");
                return Decision.Reject("Album is not monitored");
            }

            return Decision.Accept();
        }
    }
}
