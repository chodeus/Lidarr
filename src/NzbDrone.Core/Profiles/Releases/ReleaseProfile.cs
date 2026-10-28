using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Profiles.Releases
{
    public class ReleaseProfile : ModelBase
    {
        public bool Enabled { get; set; }
        public List<string> Required { get; set; }
        public List<string> Ignored { get; set; }
        public List<int> IndexerIds { get; set; }
        public HashSet<int> Tags { get; set; }
        public HashSet<int> ExcludedTags { get; set; }

        private int _storedIndexerId;

        // Develop's single indexer, kept for plugins and stored for older builds to read
        // Rows set it before the later IndexerIds column, so a read keeps the full list
        public int IndexerId
        {
            get => IndexerIds.FirstOrDefault();
            set
            {
                _storedIndexerId = value;
                IndexerIds = value > 0 ? new List<int> { value } : new List<int>();
            }
        }

        // After a read: this build stores its first indexer as IndexerId, so any other IndexerId, 0 (every indexer) too, is an older build's
        public void TakeIndexerSavedByAnOlderBuild()
        {
            if (IndexerIds.FirstOrDefault() != _storedIndexerId)
            {
                IndexerId = _storedIndexerId;
            }
        }

        public ReleaseProfile()
        {
            Enabled = true;
            Required = new List<string>();
            Ignored = new List<string>();
            Tags = new HashSet<int>();
            ExcludedTags = new HashSet<int>();
            IndexerIds = new List<int>();
        }
    }

    public class ReleaseProfilePreferredComparer : IComparer<KeyValuePair<string, int>>
    {
        public int Compare(KeyValuePair<string, int> x, KeyValuePair<string, int> y)
        {
            return y.Value.CompareTo(x.Value);
        }
    }
}
