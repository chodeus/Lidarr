using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.MetadataSource.Sources
{
    public interface IMetadataSourceFactory : IProviderFactory<IMetadataSource, MetadataSourceDefinition>
    {
    }

    public class MetadataSourceFactory : ProviderFactory<IMetadataSource, MetadataSourceDefinition>, IMetadataSourceFactory
    {
        public MetadataSourceFactory(IMetadataSourceRepository providerRepository,
                                     IEnumerable<IMetadataSource> providers,
                                     IServiceProvider container,
                                     IEventAggregator eventAggregator,
                                     Logger logger)
            : base(providerRepository, providers, container, eventAggregator, logger)
        {
        }

        protected override List<MetadataSourceDefinition> Active()
        {
            return base.Active().Where(c => c.Enable).ToList();
        }
    }
}
