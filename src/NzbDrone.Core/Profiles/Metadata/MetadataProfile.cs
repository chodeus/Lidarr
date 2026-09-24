using System.Collections.Generic;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Profiles.Metadata
{
    public class MetadataProfile : ModelBase
    {
        public string Name { get; set; }
        public List<ProfilePrimaryAlbumTypeItem> PrimaryAlbumTypes { get; set; }
        public List<ProfileSecondaryAlbumTypeItem> SecondaryAlbumTypes { get; set; }
        public List<ProfileReleaseStatusItem> ReleaseStatuses { get; set; }
        public List<string> Ignored { get; set; }

        public MetadataProfile()
        {
            Ignored = new List<string>();
        }
    }
}
