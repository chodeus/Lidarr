using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Lidarr.Api.V1.Update;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Update;
using NzbDrone.Test.Common;

namespace NzbDrone.App.Test
{
    // Not on develop: the Updates page through the real controller, provider and reader, from a real changelog.json.
    [TestFixture]
    public class BuildChangelogUpdatesFixture : TestBase<UpdateController>
    {
        private List<UpdatePackage> _updateService;

        [SetUp]
        public void Setup()
        {
            _updateService = new List<UpdatePackage> { new UpdatePackage { Version = new Version(3, 2, 1, 5196), Branch = "nightly" } };

            Mocker.GetMock<IAppFolderInfo>().SetupGet(s => s.StartUpFolder).Returns(TempFolder);
            Mocker.GetMock<IDiskProvider>().Setup(s => s.FileExists(It.IsAny<string>())).Returns<string>(File.Exists);
            Mocker.GetMock<IDiskProvider>().Setup(s => s.ReadAllText(It.IsAny<string>())).Returns<string>(File.ReadAllText);
            Mocker.GetMock<IUpdatePackageProvider>()
                  .Setup(s => s.GetRecentUpdates(It.IsAny<string>(), It.IsAny<Version>(), It.IsAny<Version>()))
                  .Returns(_updateService);

            Mocker.SetConstant<IBuildChangelog>(Mocker.Resolve<BuildChangelog>());
            Mocker.SetConstant<IRecentUpdateProvider>(Mocker.Resolve<RecentUpdateProvider>());
        }

        private void GivenChangelog(params string[] entries)
        {
            File.WriteAllText(Path.Combine(TempFolder, "changelog.json"), "[" + string.Join(",", entries) + "]");
        }

        private static string Entry(Version version, string date = "2026-11-08T04:00:00Z")
        {
            return $"{{ \"version\": \"{version}\", \"releaseDate\": \"{date}\", \"branch\": \"custom\", \"changes\": {{ \"new\": [\"a\"], \"fixed\": [] }} }}";
        }

        [Test]
        public void should_mark_the_running_build_installed()
        {
            var older = new Version(BuildInfo.Version.Major, BuildInfo.Version.Minor, BuildInfo.Version.Build, BuildInfo.Version.Revision - 1);
            GivenChangelog(Entry(BuildInfo.Version), Entry(older, "2026-11-07T04:00:00Z"));

            var resources = Subject.GetRecentUpdates();

            resources.Select(r => r.Version).Should().Equal(BuildInfo.Version, older);
            resources[0].Installed.Should().BeTrue();
            resources[0].Latest.Should().BeTrue();
            resources[0].Installable.Should().BeFalse();
            resources[1].Installed.Should().BeFalse();
        }

        [Test]
        public void should_list_a_repeated_version_once()
        {
            GivenChangelog(Entry(BuildInfo.Version, "2026-11-08T05:00:00Z"), Entry(BuildInfo.Version));

            Subject.GetRecentUpdates().Should().ContainSingle().Which.Installed.Should().BeTrue();
        }

        [Test]
        public void should_show_the_update_service_for_a_changelog_without_entries()
        {
            GivenChangelog();

            Subject.GetRecentUpdates().Select(r => r.Version).Should().Equal(new Version(3, 2, 1, 5196));
        }
    }
}
