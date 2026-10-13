using NLog;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.MetadataSource.Sources
{
    public interface IMetadataSourceRepository : IProviderRepository<MetadataSourceDefinition>
    {
    }

    public class MetadataSourceRepository : ProviderRepository<MetadataSourceDefinition>, IMetadataSourceRepository
    {
        public MetadataSourceRepository(IMainDatabase database, IEventAggregator eventAggregator, Logger logger)
            : base(database, eventAggregator, logger)
        {
        }
    }
}
