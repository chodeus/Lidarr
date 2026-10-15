using System.Linq;
using FluentAssertions;
using NLog;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Http;
using NzbDrone.Core.Blocklisting;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download.Clients.QBittorrent;
using NzbDrone.Core.Localization;
using NzbDrone.Core.MediaFiles.TorrentInfo;
using NzbDrone.Core.RemotePathMappings;

namespace NzbDrone.Core.Test.Download.DownloadClientTests.QBittorrentTests
{
    [TestFixture]
    public class QBittorrentSurfaceFixture
    {
        [Test]
        public void should_give_the_tag_call_added_to_the_develop_proxy_interface_a_default()
        {
            typeof(IQBittorrentProxy).GetMethods().Single(m => m.Name == "AddTags").IsAbstract.Should().BeFalse();
        }

        [Test]
        public void should_keep_develops_constructor()
        {
            typeof(QBittorrent).GetConstructor(new[]
            {
                typeof(IQBittorrentProxySelector),
                typeof(ITorrentFileInfoReader),
                typeof(IHttpClient),
                typeof(IConfigService),
                typeof(IDiskProvider),
                typeof(IRemotePathMappingService),
                typeof(ICacheManager),
                typeof(ILocalizationService),
                typeof(IBlocklistService),
                typeof(Logger)
            }).Should().NotBeNull();
        }
    }
}
