using System.Collections.Generic;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Update.History;

namespace NzbDrone.Core.Update
{
    public interface IRecentUpdateProvider
    {
        List<UpdatePackage> GetRecentUpdatePackages();
    }

    public class RecentUpdateProvider : IRecentUpdateProvider
    {
        private readonly IConfigFileProvider _configFileProvider;
        private readonly IUpdatePackageProvider _updatePackageProvider;
        private readonly IUpdateHistoryService _updateHistoryService;
        private readonly IBuildChangelog _buildChangelog;

        // develop's constructor, kept for plugins; the container picks the one below.
        public RecentUpdateProvider(IConfigFileProvider configFileProvider,
                                    IUpdatePackageProvider updatePackageProvider,
                                    IUpdateHistoryService updateHistoryService)
            : this(configFileProvider, updatePackageProvider, updateHistoryService, null)
        {
        }

        public RecentUpdateProvider(IConfigFileProvider configFileProvider,
                                    IUpdatePackageProvider updatePackageProvider,
                                    IUpdateHistoryService updateHistoryService,
                                    IBuildChangelog buildChangelog)
        {
            _configFileProvider = configFileProvider;
            _updatePackageProvider = updatePackageProvider;
            _updateHistoryService = updateHistoryService;
            _buildChangelog = buildChangelog;
        }

        public List<UpdatePackage> GetRecentUpdatePackages()
        {
            var changelog = _buildChangelog?.Read();

            if (changelog != null)
            {
                return changelog;
            }

            var branch = _configFileProvider.Branch;
            var version = BuildInfo.Version;
            var prevVersion = _configFileProvider.LogDbEnabled ? _updateHistoryService.PreviouslyInstalled() : null;
            return _updatePackageProvider.GetRecentUpdates(branch, version, prevVersion);
        }
    }
}
