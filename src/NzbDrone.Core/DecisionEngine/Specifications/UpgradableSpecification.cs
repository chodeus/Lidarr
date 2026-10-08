using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.DecisionEngine.Specifications
{
    public interface IUpgradableSpecification
    {
        bool IsUpgradable(QualityProfile profile, List<QualityModel> currentQualities, List<CustomFormat> currentCustomFormats, QualityModel newQuality, List<CustomFormat> newCustomFormats);
        bool QualityCutoffNotMet(QualityProfile profile, QualityModel currentQuality, QualityModel newQuality = null);
        bool CutoffNotMet(QualityProfile profile, List<QualityModel> currentQualities, List<CustomFormat> currentFormats, QualityModel newQuality = null);
        bool IsRevisionUpgrade(QualityModel currentQuality, QualityModel newQuality);
        bool IsUpgradeAllowed(QualityProfile qualityProfile, List<QualityModel> currentQualities, List<CustomFormat> currentCustomFormats, QualityModel newQuality, List<CustomFormat> newCustomFormats);
    }

    public class UpgradableSpecification : IUpgradableSpecification
    {
        private readonly IConfigService _configService;
        private readonly Logger _logger;

        public UpgradableSpecification(IConfigService configService, Logger logger)
        {
            _configService = configService;
            _logger = logger;
        }

        public bool IsUpgradable(QualityProfile qualityProfile, List<QualityModel> currentQualities, List<CustomFormat> currentCustomFormats, QualityModel newQuality, List<CustomFormat> newCustomFormats)
        {
            return GetUpgradeRejectReason(qualityProfile, currentQualities, currentCustomFormats, newQuality, newCustomFormats) == UpgradeableRejectReason.None;
        }

        public UpgradeableRejectReason GetUpgradeRejectReason(QualityProfile qualityProfile, List<QualityModel> currentQualities, List<CustomFormat> currentCustomFormats, QualityModel newQuality, List<CustomFormat> newCustomFormats)
        {
            return GetUpgradeRejectReason(qualityProfile, currentQualities.Select(q => (q, currentCustomFormats)).ToList(), newQuality, newCustomFormats);
        }

        public UpgradeableRejectReason GetUpgradeRejectReason(QualityProfile qualityProfile, List<(QualityModel Quality, List<CustomFormat> CustomFormats)> currentFiles, QualityModel newQuality, List<CustomFormat> newCustomFormats)
        {
            var newFormatScore = qualityProfile.CalculateCustomFormatScore(newCustomFormats);
            var results = currentFiles.Select(f => (Reason: GetUpgradeRejectReason(qualityProfile, f.Quality, f.CustomFormats, newQuality, newCustomFormats),
                                                    Score: qualityProfile.CalculateCustomFormatScore(f.CustomFormats)))
                                      .ToList();

            // A downgrade for any file rejects the release, a lower custom format score at the same quality included
            var downgrade = results.FirstOrDefault(r => r.Reason is UpgradeableRejectReason.BetterQuality or UpgradeableRejectReason.BetterRevision ||
                                                        (r.Reason == UpgradeableRejectReason.CustomFormatScore && newFormatScore < r.Score));

            if (downgrade.Reason != UpgradeableRejectReason.None)
            {
                return downgrade.Reason;
            }

            if (results.Empty() || results.Any(r => r.Reason == UpgradeableRejectReason.None))
            {
                return UpgradeableRejectReason.None;
            }

            return results.First().Reason;
        }

        private UpgradeableRejectReason GetUpgradeRejectReason(QualityProfile qualityProfile, QualityModel currentQuality, List<CustomFormat> currentCustomFormats, QualityModel newQuality, List<CustomFormat> newCustomFormats)
        {
            var qualityComparer = new QualityModelComparer(qualityProfile);
            var qualityCompare = qualityComparer.Compare(newQuality?.Quality, currentQuality.Quality);
            var downloadPropersAndRepacks = _configService.DownloadPropersAndRepacks;

            if (qualityCompare > 0 && QualityCutoffNotMet(qualityProfile, currentQuality, newQuality))
            {
                _logger.Debug("New item has a better quality. Existing: {0}. New: {1}", currentQuality, newQuality);
                return UpgradeableRejectReason.None;
            }

            if (qualityCompare < 0)
            {
                _logger.Debug("Existing item has better quality, skipping. Existing: {0}. New: {1}", currentQuality, newQuality);
                return UpgradeableRejectReason.BetterQuality;
            }

            var qualityRevisionCompare = newQuality?.Revision.CompareTo(currentQuality.Revision);

            // Accept unless the user doesn't want to prefer propers, optionally they can
            // use preferred words to prefer propers/repacks over non-propers/repacks.
            if (downloadPropersAndRepacks != ProperDownloadTypes.DoNotPrefer &&
                qualityRevisionCompare > 0)
            {
                _logger.Debug("New item has a better quality revision. Existing: {0}. New: {1}", currentQuality, newQuality);
                return UpgradeableRejectReason.None;
            }

            if (!qualityProfile.UpgradeAllowed)
            {
                _logger.Debug("Quality profile '{0}' does not allow upgrading. Skipping.", qualityProfile.Name);
                return UpgradeableRejectReason.UpgradesNotAllowed;
            }

            // Reject unless the user does not prefer propers/repacks and it's a revision downgrade.
            if (downloadPropersAndRepacks != ProperDownloadTypes.DoNotPrefer &&
                qualityRevisionCompare < 0)
            {
                _logger.Debug("Existing item has a better quality revision, skipping. Existing: {0}. New: {1}", currentQuality, newQuality);
                return UpgradeableRejectReason.BetterRevision;
            }

            if (qualityCompare > 0)
            {
                _logger.Debug("Existing item meets cut-off for quality, skipping. Existing: {0}. Cutoff: {1}",
                    currentQuality,
                    qualityProfile.Items[qualityProfile.GetIndex(qualityProfile.Cutoff).Index]);
                return UpgradeableRejectReason.QualityCutoff;
            }

            var currentFormatScore = qualityProfile.CalculateCustomFormatScore(currentCustomFormats);
            var newFormatScore = qualityProfile.CalculateCustomFormatScore(newCustomFormats);

            if (newFormatScore <= currentFormatScore)
            {
                _logger.Debug("New item's custom formats [{0}] ({1}) do not improve on [{2}] ({3}), skipping",
                    newCustomFormats.ConcatToString(),
                    newFormatScore,
                    currentCustomFormats.ConcatToString(),
                    currentFormatScore);
                return UpgradeableRejectReason.CustomFormatScore;
            }

            if (currentFormatScore >= qualityProfile.CutoffFormatScore)
            {
                _logger.Debug("Existing item meets cut-off for custom formats, skipping. Existing: [{0}] ({1}). Cutoff score: {2}",
                    currentCustomFormats.ConcatToString(),
                    currentFormatScore,
                    qualityProfile.CutoffFormatScore);
                return UpgradeableRejectReason.CustomFormatCutoff;
            }

            if (newFormatScore < currentFormatScore + qualityProfile.MinUpgradeFormatScore)
            {
                _logger.Debug("New item's custom formats [{0}] ({1}) do not meet minimum custom format score increment of {2} required for upgrade, skipping. Existing: [{3}] ({4}).",
                    newCustomFormats.ConcatToString(),
                    newFormatScore,
                    qualityProfile.MinUpgradeFormatScore,
                    currentCustomFormats.ConcatToString(),
                    currentFormatScore);
                return UpgradeableRejectReason.MinCustomFormatScore;
            }

            _logger.Debug("New item's custom formats [{0}] ({1}) improve on [{2}] ({3}), accepting",
                newCustomFormats.ConcatToString(),
                newFormatScore,
                currentCustomFormats.ConcatToString(),
                currentFormatScore);
            return UpgradeableRejectReason.None;
        }

        public bool QualityCutoffNotMet(QualityProfile profile, QualityModel currentQuality, QualityModel newQuality = null)
        {
            var cutoff = profile.UpgradeAllowed ? profile.Cutoff : profile.FirstAllowedQuality().Id;
            var cutoffCompare = new QualityModelComparer(profile).Compare(currentQuality.Quality.Id, cutoff);

            if (cutoffCompare < 0)
            {
                return true;
            }

            if (newQuality != null && IsRevisionUpgrade(currentQuality, newQuality))
            {
                return true;
            }

            return false;
        }

        private bool CustomFormatCutoffNotMet(QualityProfile profile, List<CustomFormat> currentFormats)
        {
            var score = profile.CalculateCustomFormatScore(currentFormats);
            var cutoff = profile.UpgradeAllowed ? profile.CutoffFormatScore : profile.MinFormatScore;

            return score < cutoff;
        }

        public bool CutoffNotMet(QualityProfile profile, List<QualityModel> currentQualities, List<CustomFormat> currentFormats, QualityModel newQuality = null)
        {
            foreach (var quality in currentQualities)
            {
                if (QualityCutoffNotMet(profile, quality, newQuality))
                {
                    return true;
                }
            }

            if (CustomFormatCutoffNotMet(profile, currentFormats))
            {
                return true;
            }

            _logger.Debug("Existing item meets cut-off, skipping. Existing: {0} [{1}] ({2})",
                currentQualities.ConcatToString(),
                currentFormats.ConcatToString(),
                profile.CalculateCustomFormatScore(currentFormats));

            return false;
        }

        public bool IsRevisionUpgrade(QualityModel currentQuality, QualityModel newQuality)
        {
            var compare = newQuality.Revision.CompareTo(currentQuality.Revision);

            // Comparing the quality directly because we don't want to upgrade to a proper for a webrip from a webdl or vice versa
            if (currentQuality.Quality == newQuality.Quality && compare > 0)
            {
                _logger.Debug("New quality is a better revision for existing quality. Existing: {0}. New: {1}", currentQuality, newQuality);
                return true;
            }

            return false;
        }

        public bool IsUpgradeAllowed(QualityProfile qualityProfile, List<QualityModel> currentQualities, List<CustomFormat> currentCustomFormats, QualityModel newQuality, List<CustomFormat> newCustomFormats)
        {
            return currentQualities.All(q => IsUpgradeAllowed(qualityProfile, q, currentCustomFormats, newQuality, newCustomFormats));
        }

        private bool IsUpgradeAllowed(QualityProfile qualityProfile, QualityModel currentQuality, List<CustomFormat> currentCustomFormats, QualityModel newQuality, List<CustomFormat> newCustomFormats)
        {
            var isQualityUpgrade = new QualityModelComparer(qualityProfile).Compare(newQuality, currentQuality) > 0;
            var isCustomFormatUpgrade = qualityProfile.CalculateCustomFormatScore(newCustomFormats) > qualityProfile.CalculateCustomFormatScore(currentCustomFormats);

            if (IsRevisionUpgrade(currentQuality, newQuality))
            {
                _logger.Debug("New quality '{0}' is a revision upgrade for '{1}'", newQuality, currentQuality);
                return true;
            }

            if ((isQualityUpgrade || isCustomFormatUpgrade) && qualityProfile.UpgradeAllowed)
            {
                _logger.Debug("Quality profile allows upgrading");
                return true;
            }

            if ((isQualityUpgrade || isCustomFormatUpgrade) && !qualityProfile.UpgradeAllowed)
            {
                _logger.Debug("Quality profile does not allow upgrades, skipping");
                return false;
            }

            return true;
        }
    }
}
