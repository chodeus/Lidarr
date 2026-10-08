using System;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.MediaFiles.TorrentInfo;

namespace NzbDrone.Core.Test.Download
{
    [TestFixture]
    public class PluginInterfaceDefaultsFixture
    {
        [TestCase(typeof(IConfigService), "get_UserRejectedExtensions")]
        [TestCase(typeof(IConfigService), "set_UserRejectedExtensions")]
        [TestCase(typeof(ITorrentIndexerSettings), "get_RejectTorrentFilesWithBlockedExtensionsWhileGrabbing")]
        [TestCase(typeof(ITorrentIndexerSettings), "set_RejectTorrentFilesWithBlockedExtensionsWhileGrabbing")]
        [TestCase(typeof(ITorrentFileInfoReader), "GetFileNamesFromTorrentFile")]
        public void should_give_members_added_to_develop_interfaces_a_default(Type type, string method)
        {
            type.GetMethod(method).IsAbstract.Should().BeFalse();
        }
    }
}
