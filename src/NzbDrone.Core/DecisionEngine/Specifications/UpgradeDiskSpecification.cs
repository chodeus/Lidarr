using System;
using System.Linq;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.DecisionEngine.Specifications
{
    public class UpgradeDiskSpecification : IDecisionEngineSpecification
    {
        private readonly IMediaFileService _mediaFileService;
        private readonly ITrackService _trackService;
        private readonly UpgradableSpecification _upgradableSpecification;
        private readonly ICustomFormatCalculationService _formatService;
        private readonly Logger _logger;
        private readonly ICached<bool> _missingFilesCache;

        public UpgradeDiskSpecification(UpgradableSpecification qualityUpgradableSpecification,
                                        IMediaFileService mediaFileService,
                                        ITrackService trackService,
                                        ICacheManager cacheManager,
                                        ICustomFormatCalculationService formatService,
                                        Logger logger)
        {
            _upgradableSpecification = qualityUpgradableSpecification;
            _mediaFileService = mediaFileService;
            _trackService = trackService;
            _formatService = formatService;
            _logger = logger;
            _missingFilesCache = cacheManager.GetCache<bool>(GetType());
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

                if (tracksMissing || !trackFiles.Any())
                {
                    continue;
                }

                var currentQualities = trackFiles.Select(c => c.Quality).Distinct().ToList();
                var currentFiles = trackFiles.Select(f => (f.Quality, CustomFormats: _formatService.ParseCustomFormat(f, subject.Artist)))
                                             .DistinctBy(f => (f.Quality, qualityProfile.CalculateCustomFormatScore(f.CustomFormats)))
                                             .ToList();

                _logger.Debug("Comparing file quality with report. Existing files contain {0}", currentQualities.ConcatToString());

                var upgradeableRejectReason = _upgradableSpecification.GetUpgradeRejectReason(qualityProfile,
                    currentFiles,
                    subject.ParsedAlbumInfo.Quality,
                    subject.CustomFormats);

                switch (upgradeableRejectReason)
                {
                    case UpgradeableRejectReason.None:
                        continue;

                    case UpgradeableRejectReason.BetterQuality:
                        return Decision.Reject("Existing files on disk is of equal or higher preference: {0}", currentQualities.ConcatToString());

                    case UpgradeableRejectReason.BetterRevision:
                        return Decision.Reject("Existing files on disk is of equal or higher revision: {0}", currentQualities.ConcatToString());

                    case UpgradeableRejectReason.QualityCutoff:
                        return Decision.Reject("Existing files on disk meets quality cutoff: {0}", qualityProfile.Items[qualityProfile.GetIndex(qualityProfile.Cutoff).Index]);

                    case UpgradeableRejectReason.CustomFormatCutoff:
                        return Decision.Reject("Existing files on disk meets Custom Format cutoff: {0}", qualityProfile.CutoffFormatScore);

                    case UpgradeableRejectReason.CustomFormatScore:
                        return Decision.Reject("Existing files on disk has a equal or higher Custom Format score: {0}", currentFiles.Max(f => qualityProfile.CalculateCustomFormatScore(f.CustomFormats)));

                    case UpgradeableRejectReason.MinCustomFormatScore:
                        return Decision.Reject("Existing files on disk has Custom Format score within Custom Format score increment: {0}", qualityProfile.MinUpgradeFormatScore);

                    case UpgradeableRejectReason.UpgradesNotAllowed:
                        return Decision.Reject("Existing files on disk and Quality Profile '{0}' does not allow upgrades", qualityProfile.Name);
                }
            }

            return Decision.Accept();
        }
    }
}
