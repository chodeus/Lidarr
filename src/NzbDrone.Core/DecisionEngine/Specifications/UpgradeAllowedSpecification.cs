using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.DecisionEngine.Specifications
{
    public class UpgradeAllowedSpecification : IDecisionEngineSpecification
    {
        private readonly UpgradableSpecification _upgradableSpecification;
        private readonly IMediaFileService _mediaFileService;
        private readonly ITrackService _trackService;
        private readonly ICustomFormatCalculationService _formatService;
        private readonly Logger _logger;
        private readonly ICached<bool> _missingFilesCache;

        public UpgradeAllowedSpecification(UpgradableSpecification upgradableSpecification,
                                           Logger logger,
                                           ICacheManager cacheManager,
                                           IMediaFileService mediaFileService,
                                           ITrackService trackService,
                                           ICustomFormatCalculationService formatService)
        {
            _upgradableSpecification = upgradableSpecification;
            _mediaFileService = mediaFileService;
            _trackService = trackService;
            _formatService = formatService;
            _missingFilesCache = cacheManager.GetCache<bool>(GetType());
            _logger = logger;
        }

        public SpecificationPriority Priority => SpecificationPriority.Default;
        public RejectionType Type => RejectionType.Permanent;

        public virtual Decision IsSatisfiedBy(RemoteAlbum subject, SearchCriteriaBase searchCriteria)
        {
            var qualityProfile = subject.Artist.QualityProfile.Value;

            foreach (var album in subject.Albums)
            {
                var tracksMissing = _missingFilesCache.Get(album.Id.ToString(),
                                                           () => _trackService.TracksWithoutFiles(album.Id).Any(),
                                                           TimeSpan.FromSeconds(30));

                var trackFiles = _mediaFileService.GetFilesByAlbum(album.Id);

                if (!tracksMissing && trackFiles.Any())
                {
                    // Get a distinct list of all current track qualities for a given album
                    var currentQualities = trackFiles.Select(c => c.Quality).Distinct().ToList();

                    _logger.Debug("Comparing file quality with report. Existing files contain {0}", currentQualities.ConcatToString());

                    var upgradeAllowed = trackFiles.Zip(_formatService.ParseCustomFormats(trackFiles, subject.Artist), (f, customFormats) => (f.Quality, CustomFormats: customFormats))
                                                   .DistinctBy(f => (f.Quality, qualityProfile.CalculateCustomFormatScore(f.CustomFormats)))
                                                   .All(f => _upgradableSpecification.IsUpgradeAllowed(qualityProfile,
                                                                                                       new List<QualityModel> { f.Quality },
                                                                                                       f.CustomFormats,
                                                                                                       subject.ParsedAlbumInfo.Quality,
                                                                                                       subject.CustomFormats));

                    if (!upgradeAllowed)
                    {
                        _logger.Debug("Upgrading is not allowed by the quality profile");

                        return Decision.Reject("Existing files and the Quality profile does not allow upgrades");
                    }
                }
            }

            return Decision.Accept();
        }
    }
}
