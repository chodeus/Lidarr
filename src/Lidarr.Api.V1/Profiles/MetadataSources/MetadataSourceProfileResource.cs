using System.Collections.Generic;
using System.Linq;
using Lidarr.Http.REST;
using NzbDrone.Core.Profiles.MetadataSources;

namespace Lidarr.Api.V1.Profiles.MetadataSources
{
    public class MetadataSourceProfileResource : RestResource
    {
        public string Name { get; set; }
        public HashSet<int> Tags { get; set; }
        public int Order { get; set; }
        public MetadataSourceCombination PrimaryCombination { get; set; }
        public List<MetadataSourceProfileItemResource> Items { get; set; }
    }

    public class MetadataSourceProfileItemResource
    {
        public int MetadataSourceId { get; set; }
        public MetadataSourceRole Role { get; set; }
    }

    public static class MetadataSourceProfileResourceMapper
    {
        public static MetadataSourceProfileResource ToResource(this MetadataSourceProfile model)
        {
            if (model == null)
            {
                return null;
            }

            return new MetadataSourceProfileResource
            {
                Id = model.Id,
                Name = model.Name,
                Tags = new HashSet<int>(model.Tags),
                Order = model.Order,
                PrimaryCombination = model.PrimaryCombination,
                Items = model.Items.Select(i => new MetadataSourceProfileItemResource { MetadataSourceId = i.MetadataSourceId, Role = i.Role }).ToList()
            };
        }

        public static MetadataSourceProfile ToModel(this MetadataSourceProfileResource resource)
        {
            if (resource == null)
            {
                return null;
            }

            return new MetadataSourceProfile
            {
                Id = resource.Id,
                Name = resource.Name,
                Tags = new HashSet<int>(resource.Tags ?? new HashSet<int>()),
                Order = resource.Order,
                PrimaryCombination = resource.PrimaryCombination,
                Items = (resource.Items ?? new List<MetadataSourceProfileItemResource>())
                    .Select(i => new MetadataSourceProfileItem { MetadataSourceId = i.MetadataSourceId, Role = i.Role })
                    .ToList()
            };
        }

        public static List<MetadataSourceProfileResource> ToResource(this IEnumerable<MetadataSourceProfile> models)
        {
            return models.Select(ToResource).ToList();
        }
    }
}
