using System;
using System.Collections.Generic;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.Indexers
{
    public interface IIndexerSettings : IProviderConfig
    {
        string BaseUrl { get; set; }
        int? EarlyReleaseLimit { get; set; }

        // Default keeps plugin settings compiled against develop loading; Lidarr's own indexers override it
        IEnumerable<int> FailDownloads
        {
            get => Array.Empty<int>();
            set { }
        }
    }
}
