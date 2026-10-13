using System.Collections.Generic;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Profiles.MetadataSources
{
    public class MetadataSourceProfile : ModelBase
    {
        public string Name { get; set; }
        public HashSet<int> Tags { get; set; }
        public int Order { get; set; }
        public MetadataSourceCombination PrimaryCombination { get; set; }

        // Order matters: sources are asked in this order within their role
        public List<MetadataSourceProfileItem> Items { get; set; }

        public MetadataSourceProfile()
        {
            Tags = new HashSet<int>();
            Items = new List<MetadataSourceProfileItem>();
        }
    }

    public class MetadataSourceProfileItem : IEmbeddedDocument
    {
        public int MetadataSourceId { get; set; }
        public MetadataSourceRole Role { get; set; }
    }

    public enum MetadataSourceRole
    {
        Primary = 0,
        Fallback = 1
    }

    public enum MetadataSourceCombination
    {
        Or = 0,
        And = 1
    }
}
