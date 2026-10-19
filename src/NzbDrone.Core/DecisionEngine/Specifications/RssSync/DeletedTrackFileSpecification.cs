using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.DecisionEngine.Specifications.RssSync
{
    public class DeletedTrackFileSpecification : IDecisionEngineSpecification
    {
        private readonly IDiskProvider _diskProvider;
        private readonly IConfigService _configService;
        private readonly IMediaFileService _albumService;
        private readonly IUnmonitorDeletedAlbumsService _unmonitorDeletedAlbumsService;
        private readonly Logger _logger;

        public DeletedTrackFileSpecification(IDiskProvider diskProvider,
                                             IConfigService configService,
                                             IMediaFileService albumService,
                                             IUnmonitorDeletedAlbumsService unmonitorDeletedAlbumsService,
                                             Logger logger)
        {
            _diskProvider = diskProvider;
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
                _logger.Debug("Skipping deleted trackfile check during search");
                return Decision.Accept();
            }

            // A scan has already dropped these files from the database, and has yet to decide whether to unmonitor the album.
            if (subject.Albums.Any(v => _unmonitorDeletedAlbumsService.IsMissingFromDisk(v.Id)))
            {
                _logger.Debug("Files for this album are missing from disk, waiting for a disk scan to unmonitor it. skipping.");
                return Decision.Reject("Album files are missing from disk");
            }

            var missingTrackFiles = subject.Albums
                                             .SelectMany(v => _albumService.GetFilesByAlbum(v.Id))
                                             .DistinctBy(v => v.Id)
                                             .Where(v => IsTrackFileMissing(subject.Artist, v))
                                             .ToArray();

            if (missingTrackFiles.Any())
            {
                foreach (var missingTrackFile in missingTrackFiles)
                {
                    _logger.Trace("Track file {0} is missing from disk.", missingTrackFile.Path);
                }

                _logger.Debug("Files for this album exist in the database but not on disk, will be unmonitored on next diskscan. skipping.");
                return Decision.Reject("Artist is not monitored");
            }

            return Decision.Accept();
        }

        private bool IsTrackFileMissing(Artist artist, TrackFile trackFile)
        {
            return !_diskProvider.FileExists(trackFile.Path);
        }
    }
}
