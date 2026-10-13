using System.Collections.Generic;
using System.Linq;
using FluentValidation;
using Lidarr.Http;
using Lidarr.Http.REST;
using Lidarr.Http.REST.Attributes;
using Lidarr.Http.Validation;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.MetadataSource.Sources;
using NzbDrone.Core.Profiles.MetadataSources;

namespace Lidarr.Api.V1.Profiles.MetadataSources
{
    [V1ApiController]
    public class MetadataSourceProfileController : RestController<MetadataSourceProfileResource>
    {
        private readonly IMetadataSourceProfileService _profileService;
        private readonly IMetadataSourceFactory _sourceFactory;

        public MetadataSourceProfileController(IMetadataSourceProfileService profileService, IMetadataSourceFactory sourceFactory)
        {
            _profileService = profileService;
            _sourceFactory = sourceFactory;

            SharedValidator.RuleFor(d => d.Name).NotEmpty();
            SharedValidator.RuleFor(d => d.Tags).NotEmpty().When(d => d.Id != MetadataSourceProfileService.DefaultProfileId);
            SharedValidator.RuleFor(d => d.Tags).EmptyCollection<MetadataSourceProfileResource, int>().When(d => d.Id == MetadataSourceProfileService.DefaultProfileId);
            SharedValidator.RuleFor(d => d.Items).Cascade(CascadeMode.Stop)
                .NotNull()
                .Must(items => items.Any(i => i.Role == MetadataSourceRole.Primary)).WithMessage("Add at least one primary source")
                .Must(items => items.Select(i => i.MetadataSourceId).Distinct().Count() == items.Count).WithMessage("Each source can be in a profile only once")
                .Must(items => items.All(i => _sourceFactory.Exists(i.MetadataSourceId))).WithMessage("Unknown metadata source")
                .Must(HaveOneIdFamilyForPrimaries).WithMessage("Primary sources must use the same kind of IDs, such as MusicBrainz");
        }

        [RestPostById]
        public ActionResult<MetadataSourceProfileResource> Create([FromBody] MetadataSourceProfileResource resource)
        {
            var model = _profileService.Add(resource.ToModel());

            return Created(model.Id);
        }

        [RestDeleteById]
        public void DeleteProfile(int id)
        {
            if (id == MetadataSourceProfileService.DefaultProfileId)
            {
                throw new MethodNotAllowedException("Cannot delete the default metadata source profile");
            }

            _profileService.Delete(id);
        }

        [RestPutById]
        public ActionResult<MetadataSourceProfileResource> Update([FromBody] MetadataSourceProfileResource resource)
        {
            var model = resource.ToModel();
            _profileService.Update(model);

            return Accepted(model.Id);
        }

        public override MetadataSourceProfileResource GetResourceById(int id)
        {
            return _profileService.Get(id).ToResource();
        }

        [HttpGet]
        public List<MetadataSourceProfileResource> GetAll()
        {
            return _profileService.All().ToResource();
        }

        [HttpPut("reorder/{id:int}")]
        public object Reorder(int id, [FromQuery] int? afterId = null)
        {
            ValidateId(id);

            return _profileService.Reorder(id, afterId).ToResource();
        }

        private bool HaveOneIdFamilyForPrimaries(List<MetadataSourceProfileItemResource> items)
        {
            return items.Where(i => i.Role == MetadataSourceRole.Primary)
                .Select(i => _sourceFactory.GetInstance(_sourceFactory.Get(i.MetadataSourceId)).IdFamily)
                .Distinct()
                .Count() <= 1;
        }
    }
}
