using NzbDrone.Core.MetadataSource.SkyHook;
using NzbDrone.Core.ThingiProvider;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.MetadataSource.Sources.Lidarr
{
    public class LidarrMetadataSourceSettings : IProviderConfig
    {
        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult();
        }
    }

    // Lidarr's own metadata server, through the lookup Lidarr already uses
    public class LidarrMetadataSource : SkyHookMetadataSourceBase<LidarrMetadataSourceSettings>
    {
        private readonly SkyHookProxy _skyHook;

        public LidarrMetadataSource(SkyHookProxy skyHook)
        {
            _skyHook = skyHook;
        }

        public override string Name => "Lidarr";

        protected override SkyHookProxy SkyHook => _skyHook;
    }
}
