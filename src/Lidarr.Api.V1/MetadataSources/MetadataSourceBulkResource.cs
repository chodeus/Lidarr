using NzbDrone.Core.MetadataSource.Sources;

namespace Lidarr.Api.V1.MetadataSources
{
    public class MetadataSourceBulkResource : ProviderBulkResource<MetadataSourceBulkResource>
    {
    }

    public class MetadataSourceBulkResourceMapper : ProviderBulkResourceMapper<MetadataSourceBulkResource, MetadataSourceDefinition>
    {
    }
}
