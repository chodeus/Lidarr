using System.Linq;
using FluentValidation;
using Lidarr.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.Tags;
using NzbDrone.Core.Validation;

namespace Lidarr.Api.V1.Config
{
    [V1ApiController("config/importlist")]
    public class ImportListConfigController : ConfigController<ImportListConfigResource>
    {
        public ImportListConfigController(IConfigService configService, ITagService tagService)
            : base(configService)
        {
            SharedValidator.RuleFor(x => x.ListSyncTag)
               .ValidId()
               .WithMessage("Tag must be specified")
               .Must(id => tagService.All().Any(t => t.Id == id))
               .WithMessage("Tag must exist")
               .When(x => x.ListSyncLevel == ListSyncLevelType.KeepAndTag);
        }

        protected override ImportListConfigResource ToResource(IConfigService model)
        {
            return ImportListConfigResourceMapper.ToResource(model);
        }
    }
}
