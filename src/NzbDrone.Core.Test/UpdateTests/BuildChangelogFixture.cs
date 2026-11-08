using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Update;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.UpdateTests
{
    [TestFixture]
    public class BuildChangelogFixture : CoreTest<BuildChangelog>
    {
        // The shape build.sh writes.
        private const string Changelog = @"[
  { ""version"": ""10.0.0.162"", ""releaseDate"": ""2026-11-08T04:00:00Z"", ""branch"": ""custom"",
    ""changes"": { ""new"": [""Build changelog on the Updates page""], ""fixed"": [""Upstream: Numeric ID3 genres on the first tagged file""] } },
  { ""version"": ""10.0.0.161"", ""releaseDate"": ""2026-11-07T11:53:20Z"", ""branch"": ""custom"",
    ""changes"": { ""new"": [], ""fixed"": [] } }
]";

        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IAppFolderInfo>().SetupGet(s => s.StartUpFolder).Returns(TempFolder);
            Mocker.GetMock<IDiskProvider>().Setup(s => s.FileExists(It.IsAny<string>())).Returns<string>(File.Exists);
            Mocker.GetMock<IDiskProvider>().Setup(s => s.ReadAllText(It.IsAny<string>())).Returns<string>(File.ReadAllText);
        }

        private void GivenChangelog(string content)
        {
            // The name build.sh writes, not the constant, so a renamed constant fails here.
            File.WriteAllText(Path.Combine(TempFolder, "changelog.json"), content);
        }

        [Test]
        public void should_read_the_changelog_beside_the_binaries()
        {
            GivenChangelog(Changelog);

            var packages = Subject.Read();

            packages.Should().HaveCount(2);
            packages[0].Version.Should().Be(new Version(10, 0, 0, 162));
            packages[0].ReleaseDate.Should().Be(new DateTime(2026, 11, 8, 4, 0, 0, DateTimeKind.Utc));
            packages[0].Branch.Should().Be("custom");
            packages[0].Changes.New.Should().Equal("Build changelog on the Updates page");
            packages[0].Changes.Fixed.Should().Equal("Upstream: Numeric ID3 genres on the first tagged file");
            packages[1].Version.Should().Be(new Version(10, 0, 0, 161));
            packages[1].Changes.New.Should().BeEmpty();
        }

        [Test]
        public void should_return_null_without_a_changelog()
        {
            Subject.Read().Should().BeNull();
        }

        [Test]
        public void should_return_null_and_warn_for_a_malformed_changelog()
        {
            GivenChangelog("[{ \"version\": ");

            Subject.Read().Should().BeNull();

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_return_null_and_warn_when_the_changelog_cannot_be_read()
        {
            GivenChangelog(Changelog);
            Mocker.GetMock<IDiskProvider>().Setup(s => s.ReadAllText(It.IsAny<string>())).Throws(new UnauthorizedAccessException("Access to the path is denied."));

            Subject.Read().Should().BeNull();

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_keep_the_first_entry_of_a_repeated_version()
        {
            GivenChangelog(@"[
  { ""version"": ""10.0.0.162"", ""releaseDate"": ""2026-11-08T05:00:00Z"", ""changes"": { ""new"": [""second run""], ""fixed"": [] } },
  { ""version"": ""10.0.0.162"", ""releaseDate"": ""2026-11-08T04:00:00Z"", ""changes"": { ""new"": [""first run""], ""fixed"": [] } }
]");

            var packages = Subject.Read();

            packages.Should().ContainSingle();
            packages[0].Changes.New.Should().Equal("second run");
        }

        [Test]
        public void should_skip_entries_without_a_version()
        {
            GivenChangelog(@"[
  { ""version"": ""10.0.0.162"", ""releaseDate"": ""2026-11-08T04:00:00Z"" },
  null,
  { ""releaseDate"": ""2026-11-07T04:00:00Z"" }
]");

            Subject.Read().Select(p => p.Version).Should().Equal(new Version(10, 0, 0, 162));
        }

        [TestCase("[]")]
        [TestCase("[null]")]
        [TestCase("null")]
        public void should_return_null_for_a_changelog_without_entries(string content)
        {
            GivenChangelog(content);

            Subject.Read().Should().BeNull();
        }
    }
}
