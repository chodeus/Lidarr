using System;
using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Update;

namespace NzbDrone.Core.Test.UpdateTests
{
    [TestFixture]
    public class RecentUpdateProviderFixture : CoreTest<RecentUpdateProvider>
    {
        private List<UpdatePackage> _updateService;

        [SetUp]
        public void Setup()
        {
            _updateService = new List<UpdatePackage> { new UpdatePackage { Version = new Version(3, 2, 1, 5196) } };

            Mocker.GetMock<IConfigFileProvider>().SetupGet(s => s.Branch).Returns("nightly");
            Mocker.GetMock<IUpdatePackageProvider>()
                  .Setup(s => s.GetRecentUpdates(It.IsAny<string>(), It.IsAny<Version>(), It.IsAny<Version>()))
                  .Returns(_updateService);
        }

        [Test]
        public void should_return_the_build_changelog_instead_of_the_update_service()
        {
            var changelog = new List<UpdatePackage> { new UpdatePackage { Version = new Version(10, 0, 0, 162) } };
            Mocker.GetMock<IBuildChangelog>().Setup(s => s.Read()).Returns(changelog);

            Subject.GetRecentUpdatePackages().Should().BeSameAs(changelog);

            Mocker.GetMock<IUpdatePackageProvider>()
                  .Verify(s => s.GetRecentUpdates(It.IsAny<string>(), It.IsAny<Version>(), It.IsAny<Version>()), Times.Never());
        }

        [Test]
        public void should_ask_the_update_service_without_a_build_changelog()
        {
            Mocker.GetMock<IBuildChangelog>().Setup(s => s.Read()).Returns((List<UpdatePackage>)null);

            Subject.GetRecentUpdatePackages().Should().BeSameAs(_updateService);

            Mocker.GetMock<IUpdatePackageProvider>()
                  .Verify(s => s.GetRecentUpdates("nightly", It.IsAny<Version>(), It.IsAny<Version>()), Times.Once());
        }
    }
}
