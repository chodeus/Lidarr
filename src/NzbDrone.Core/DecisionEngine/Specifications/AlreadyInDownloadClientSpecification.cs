using NLog;
using NzbDrone.Core.Download;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.DecisionEngine.Specifications
{
    public class AlreadyInDownloadClientSpecification : IDecisionEngineSpecification
    {
        private readonly IDuplicateReleaseCache _duplicateReleaseCache;
        private readonly Logger _logger;

        public AlreadyInDownloadClientSpecification(IDuplicateReleaseCache duplicateReleaseCache, Logger logger)
        {
            _duplicateReleaseCache = duplicateReleaseCache;
            _logger = logger;
        }

        public SpecificationPriority Priority => SpecificationPriority.Default;
        public RejectionType Type => RejectionType.Permanent;

        public Decision IsSatisfiedBy(RemoteAlbum subject, SearchCriteriaBase searchCriteria)
        {
            var downloadClient = _duplicateReleaseCache.Find(subject.Release);

            if (downloadClient == null)
            {
                return Decision.Accept();
            }

            _logger.Debug("{0} already has {1}, rejecting.", downloadClient, subject.Release.Title);

            return Decision.Reject("{0} already has this release", downloadClient);
        }
    }
}
