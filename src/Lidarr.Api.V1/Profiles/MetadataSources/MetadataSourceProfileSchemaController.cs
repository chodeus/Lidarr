using System.Collections.Generic;
using Lidarr.Http;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Profiles.MetadataSources;

namespace Lidarr.Api.V1.Profiles.MetadataSources
{
    [V1ApiController("metadatasourceprofile/schema")]
    public class MetadataSourceProfileSchemaController : Controller
    {
        private readonly IMetadataSourceProfileService _profileService;

        public MetadataSourceProfileSchemaController(IMetadataSourceProfileService profileService)
        {
            _profileService = profileService;
        }

        // A new profile starts with the default profile's sources
        [HttpGet]
        [Produces("application/json")]
        public MetadataSourceProfileResource GetSchema()
        {
            var resource = _profileService.GetDefaultProfile().ToResource();

            resource.Id = 0;
            resource.Name = string.Empty;
            resource.Order = 0;
            resource.Tags = new HashSet<int>();

            return resource;
        }
    }
}
