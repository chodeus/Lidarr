using System;
using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.Custom;
using NzbDrone.Core.ImportLists.Lidarr;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ImportListTests
{
    [TestFixture]
    public class ImportListFetchFailureFixture : CoreTest
    {
        [TestCase(false)]
        [TestCase(true)]
        public void custom_list_should_report_failure_when_it_errors(bool fails)
        {
            var setup = Mocker.GetMock<ICustomImportProxy>().Setup(v => v.GetArtists(It.IsAny<CustomSettings>()));

            if (fails)
            {
                setup.Throws(new InvalidOperationException());
            }
            else
            {
                setup.Returns(new List<CustomArtist>());
            }

            var list = Mocker.Resolve<CustomImport>();
            list.Definition = new ImportListDefinition { Id = 1, Settings = new CustomSettings() };

            list.Fetch().AnyFailure.Should().Be(fails);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void lidarr_list_should_report_failure_when_it_errors(bool fails)
        {
            var setup = Mocker.GetMock<ILidarrV1Proxy>().Setup(v => v.GetAlbums(It.IsAny<LidarrSettings>()));

            if (fails)
            {
                setup.Throws(new InvalidOperationException());
            }
            else
            {
                setup.Returns(new List<LidarrAlbum>());
            }

            Mocker.GetMock<ILidarrV1Proxy>()
                .Setup(v => v.GetArtists(It.IsAny<LidarrSettings>()))
                .Returns(new List<LidarrArtist>());

            var list = Mocker.Resolve<LidarrImport>();
            list.Definition = new ImportListDefinition { Id = 1, Settings = new LidarrSettings() };

            list.Fetch().AnyFailure.Should().Be(fails);
        }
    }
}
