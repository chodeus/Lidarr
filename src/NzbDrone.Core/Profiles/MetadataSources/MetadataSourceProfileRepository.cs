using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Profiles.MetadataSources
{
    public interface IMetadataSourceProfileRepository : IBasicRepository<MetadataSourceProfile>
    {
    }

    public class MetadataSourceProfileRepository : BasicRepository<MetadataSourceProfile>, IMetadataSourceProfileRepository
    {
        public MetadataSourceProfileRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }
    }
}
