using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.Indexers;

namespace NzbDrone.Core.Test.Download
{
    [TestFixture]
    public class DownloadClientProviderSurfaceFixture
    {
        [TestCase("GetDownloadClients", 4)]
        [TestCase("ReportSuccessfulDownloadClient", 2)]
        public void should_give_members_added_to_the_develop_interface_a_default(string method, int parameters)
        {
            typeof(IProvideDownloadClient).GetMethods().Single(m => m.Name == method && m.GetParameters().Length == parameters).IsAbstract.Should().BeFalse();
        }

        [Test]
        public void should_list_the_single_client_for_a_provider_built_against_develop()
        {
            var client = new Mock<IDownloadClient>().Object;
            var provider = new Mock<IProvideDownloadClient> { CallBase = true };
            provider.Setup(p => p.GetDownloadClient(nameof(UsenetDownloadProtocol), 1, false, null)).Returns(client);

            provider.Object.GetDownloadClients(nameof(UsenetDownloadProtocol), 1).Should().Equal(client);
        }

        [Test]
        public void should_list_no_clients_for_a_provider_built_against_develop_without_one()
        {
            var provider = new Mock<IProvideDownloadClient> { CallBase = true };

            provider.Object.GetDownloadClients(nameof(UsenetDownloadProtocol), 1, false, new HashSet<int>()).Should().BeEmpty();
        }
    }
}
