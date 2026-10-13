using NzbDrone.Core.MetadataSource.Sources;

namespace Lidarr.Api.V1.MetadataSources
{
    public class MetadataSourceResource : ProviderResource<MetadataSourceResource>
    {
        public bool Enable { get; set; }
    }

    public class MetadataSourceResourceMapper : ProviderResourceMapper<MetadataSourceResource, MetadataSourceDefinition>
    {
        public override MetadataSourceResource ToResource(MetadataSourceDefinition definition)
        {
            if (definition == null)
            {
                return null;
            }

            var resource = base.ToResource(definition);

            resource.Enable = definition.Enable;

            return resource;
        }

        public override MetadataSourceDefinition ToModel(MetadataSourceResource resource, MetadataSourceDefinition existingDefinition)
        {
            if (resource == null)
            {
                return null;
            }

            var definition = base.ToModel(resource, existingDefinition);

            definition.Enable = resource.Enable;

            return definition;
        }
    }
}
