using System.Collections.Generic;
using System.Linq;
using FluentValidation;
using Lidarr.Http;
using Lidarr.Http.REST;
using Lidarr.Http.REST.Attributes;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Profiles.Releases;

namespace Lidarr.Api.V1.Profiles.Release
{
    [V1ApiController]
    public class ReleaseProfileController : RestController<ReleaseProfileResource>
    {
        private readonly IReleaseProfileService _releaseProfileService;
        private readonly IIndexerFactory _indexerFactory;

        public ReleaseProfileController(IReleaseProfileService releaseProfileService, IIndexerFactory indexerFactory)
        {
            _releaseProfileService = releaseProfileService;
            _indexerFactory = indexerFactory;

            SharedValidator.RuleFor(r => r).Custom((restriction, context) =>
            {
                if (restriction.Ignored.Empty() && restriction.Required.Empty())
                {
                    context.AddFailure("Either 'Must contain' or 'Must not contain' is required");
                }

                if (restriction.Enabled)
                {
                    foreach (var indexerId in restriction.MapIndexerIds().Where(i => !_indexerFactory.Exists(i)))
                    {
                        context.AddFailure(nameof(ReleaseProfile.IndexerIds), $"Indexer does not exist: {indexerId}");
                    }
                }
            });

            SharedValidator.RuleFor(r => r.ExcludedTags)
                .Cascade(CascadeMode.Stop)
                .NotNull()
                .Must((restriction, excludedTags) => restriction.Tags == null || !excludedTags.Overlaps(restriction.Tags))
                .WithMessage("A tag cannot be in both 'Tags' and 'Excluded Tags'");
        }

        public override ReleaseProfileResource GetResourceById(int id)
        {
            return _releaseProfileService.Get(id).ToResource();
        }

        [HttpGet]
        public List<ReleaseProfileResource> GetAll()
        {
            return _releaseProfileService.All().ToResource();
        }

        [RestPostById]
        public ActionResult<ReleaseProfileResource> Create([FromBody] ReleaseProfileResource resource)
        {
            return Created(_releaseProfileService.Add(resource.ToModel()).Id);
        }

        [RestPutById]
        public ActionResult<ReleaseProfileResource> Update([FromBody] ReleaseProfileResource resource)
        {
            _releaseProfileService.Update(resource.ToModel());
            return Accepted(resource.Id);
        }

        [RestDeleteById]
        public void DeleteById(int id)
        {
            _releaseProfileService.Delete(id);
        }
    }
}
