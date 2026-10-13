using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.RootFolderTests
{
    [TestFixture]
    public class GetBestRootFolderPathFixture : CoreTest<RootFolderService>
    {
        private void GivenRootFolders(params string[] paths)
        {
            Mocker.GetMock<IRootFolderRepository>()
                .Setup(s => s.All())
                .Returns(paths.Select(p => new RootFolder { Path = p }));
        }

        [Test]
        public void should_use_the_given_root_folders_without_loading_them()
        {
            var rootFolders = new List<RootFolder> { new RootFolder { Path = @"C:\Test\Music".AsOsAgnostic() } };

            Subject.GetBestRootFolderPath(@"C:\Test\Music\Artist Name".AsOsAgnostic(), rootFolders).Should().Be(@"C:\Test\Music".AsOsAgnostic());

            Mocker.GetMock<IRootFolderRepository>().Verify(s => s.All(), Times.Never());
        }

        [Test]
        public void should_keep_the_single_path_overload_and_default_the_list_overload()
        {
            var type = typeof(IRootFolderService);

            type.GetMethod(nameof(IRootFolderService.GetBestRootFolderPath), new[] { typeof(string) }).Should().NotBeNull();
            type.GetMethod(nameof(IRootFolderService.GetBestRootFolderPath), new[] { typeof(string), typeof(List<RootFolder>) }).IsAbstract.Should().BeFalse();
        }

        [Test]
        public void should_return_root_folder_that_is_parent_path()
        {
            GivenRootFolders(@"C:\Test\Music".AsOsAgnostic(), @"D:\Test\Music".AsOsAgnostic());
            Subject.GetBestRootFolderPath(@"C:\Test\Music\Series Title".AsOsAgnostic()).Should().Be(@"C:\Test\Music".AsOsAgnostic());
        }

        [Test]
        public void should_return_root_folder_that_is_grandparent_path()
        {
            GivenRootFolders(@"C:\Test\Music".AsOsAgnostic(), @"D:\Test\Music".AsOsAgnostic());
            Subject.GetBestRootFolderPath(@"C:\Test\Music\S\Series Title".AsOsAgnostic()).Should().Be(@"C:\Test\Music".AsOsAgnostic());
        }

        [Test]
        public void should_get_parent_path_from_os_path_if_matching_root_folder_is_not_found()
        {
            var artistPath = @"T:\Test\Music\Artist Title".AsOsAgnostic();

            GivenRootFolders(@"C:\Test\Music".AsOsAgnostic(), @"D:\Test\Music".AsOsAgnostic());
            Subject.GetBestRootFolderPath(artistPath).Should().Be(@"T:\Test\Music".AsOsAgnostic());
        }

        [Test]
        public void should_get_parent_path_from_os_path_if_matching_root_folder_is_not_found_for_posix_path()
        {
            WindowsOnly();

            var artistPath = "/mnt/music/Artist Title";

            GivenRootFolders(@"C:\Test\Music".AsOsAgnostic(), @"D:\Test\Music".AsOsAgnostic());
            Subject.GetBestRootFolderPath(artistPath).Should().Be(@"/mnt/music");
        }

        [Test]
        public void should_get_parent_path_from_os_path_if_matching_root_folder_is_not_found_for_windows_path()
        {
            PosixOnly();

            var artistPath = @"T:\Test\Music\Artist Title";

            GivenRootFolders(@"C:\Test\Music".AsOsAgnostic(), @"D:\Test\Music".AsOsAgnostic());
            Subject.GetBestRootFolderPath(artistPath).Should().Be(@"T:\Test\Music");
        }
    }
}
