using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Serializer;

namespace NzbDrone.Core.Update
{
    public interface IBuildChangelog
    {
        List<UpdatePackage> Read();
    }

    public class BuildChangelog : IBuildChangelog
    {
        // The custom image's changelog.py writes it beside the binaries.
        public const string FileName = "changelog.json";

        private readonly IAppFolderInfo _appFolderInfo;
        private readonly IDiskProvider _diskProvider;
        private readonly Logger _logger;

        public BuildChangelog(IAppFolderInfo appFolderInfo, IDiskProvider diskProvider, Logger logger)
        {
            _appFolderInfo = appFolderInfo;
            _diskProvider = diskProvider;
            _logger = logger;
        }

        public List<UpdatePackage> Read()
        {
            var path = Path.Combine(_appFolderInfo.StartUpFolder, FileName);

            if (!_diskProvider.FileExists(path))
            {
                return null;
            }

            try
            {
                return Json.Deserialize<List<UpdatePackage>>(_diskProvider.ReadAllText(path));
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                _logger.Warn(ex, "Unable to read {0}, showing the update service's changes instead", path);
                return null;
            }
        }
    }
}
