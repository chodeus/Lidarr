using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Profiles.Metadata;
using NzbDrone.Core.Profiles.Releases;

namespace NzbDrone.Core.MetadataSource
{
    // The album types, release statuses and titles a metadata profile allows, applied the same way by every metadata source
    public class MetadataProfileAlbumFilter
    {
        private readonly HashSet<string> _primaryTypes;
        private readonly HashSet<string> _secondaryTypes;
        private readonly HashSet<string> _releaseStatuses;
        private readonly List<string> _ignored;

        public MetadataProfileAlbumFilter(MetadataProfile metadataProfile)
        {
            _primaryTypes = new HashSet<string>(metadataProfile.PrimaryAlbumTypes.Where(s => s.Allowed).Select(s => s.PrimaryAlbumType.Name));
            _secondaryTypes = new HashSet<string>(metadataProfile.SecondaryAlbumTypes.Where(s => s.Allowed).Select(s => s.SecondaryAlbumType.Name));
            _releaseStatuses = new HashSet<string>(metadataProfile.ReleaseStatuses.Where(s => s.Allowed).Select(s => s.ReleaseStatus.Name));
            _ignored = metadataProfile.Ignored ?? new List<string>();
        }

        public static MetadataProfileAlbumFilter For(IMetadataProfileService metadataProfileService, int metadataProfileId)
        {
            var metadataProfile = metadataProfileService.Exists(metadataProfileId) ? metadataProfileService.Get(metadataProfileId) : metadataProfileService.All().First();

            return new MetadataProfileAlbumFilter(metadataProfile);
        }

        public bool Allows(string primaryType, ICollection<string> secondaryTypes, IEnumerable<string> releaseStatuses)
        {
            return _primaryTypes.Contains(primaryType) &&
                   ((!secondaryTypes.Any() && _secondaryTypes.Contains("Studio")) || secondaryTypes.Any(_secondaryTypes.Contains)) &&
                   releaseStatuses.Any(_releaseStatuses.Contains);
        }

        public bool AllowsReleaseStatus(string releaseStatus)
        {
            return _releaseStatuses.Contains(releaseStatus);
        }

        // Must Not Contain terms match whole words, as in SkyHook's own filter
        public bool AllowsTitle(string title, TermMatcherService termMatcher)
        {
            return !_ignored.Any(term => termMatcher.IsWholeWordMatch(term, title));
        }
    }
}
