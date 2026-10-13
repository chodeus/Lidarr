using System;
using Lidarr.Http;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.MetadataSource.Sources;
using NzbDrone.SignalR;

namespace Lidarr.Api.V1.MetadataSources
{
    [V1ApiController]
    public class MetadataSourceController : ProviderControllerBase<MetadataSourceResource, MetadataSourceBulkResource, IMetadataSource, MetadataSourceDefinition>
    {
        public static readonly MetadataSourceResourceMapper ResourceMapper = new();
        public static readonly MetadataSourceBulkResourceMapper BulkResourceMapper = new();

        public MetadataSourceController(IBroadcastSignalRMessage signalRBroadcaster, IMetadataSourceFactory metadataSourceFactory)
            : base(signalRBroadcaster, metadataSourceFactory, "metadatasource", ResourceMapper, BulkResourceMapper)
        {
        }

        [NonAction]
        public override ActionResult<MetadataSourceResource> UpdateProvider([FromBody] MetadataSourceBulkResource providerResource)
        {
            throw new NotImplementedException();
        }

        [NonAction]
        public override object DeleteProviders([FromBody] MetadataSourceBulkResource resource)
        {
            throw new NotImplementedException();
        }
    }
}
