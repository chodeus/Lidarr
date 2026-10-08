using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles
{
    [TestFixture]
    public class FileExtensionsFixture : CoreTest
    {
        [Test]
        public void should_group_unsafe_files_by_fail_downloads_type()
        {
            var found = FileExtensions.FindUnsafeExtensions(new[] { "01 - Track.flac", "Setup.EXE", "run.ps1", "Album.nfo", "README" }, ".nfo");

            found.Keys.Should().BeEquivalentTo(new[] { FailDownloads.Executables, FailDownloads.PotentiallyDangerous, FailDownloads.UserDefinedExtensions });
            found[FailDownloads.Executables].Should().BeEquivalentTo(".EXE");
            found[FailDownloads.PotentiallyDangerous].Should().BeEquivalentTo(".ps1");
            found[FailDownloads.UserDefinedExtensions].Should().BeEquivalentTo(".nfo");
        }

        [Test]
        public void should_put_an_extension_in_every_group_that_lists_it()
        {
            var found = FileExtensions.FindUnsafeExtensions(new[] { "Setup.exe" }, "exe");

            found.Keys.Should().BeEquivalentTo(new[] { FailDownloads.Executables, FailDownloads.UserDefinedExtensions });
        }

        [TestCase(null)]
        [TestCase("")]
        public void should_find_nothing_in_safe_files_without_user_extensions(string userRejectedExtensions)
        {
            FileExtensions.FindUnsafeExtensions(new[] { "01 - Track.flac", "cover.jpg", "Album.nfo" }, userRejectedExtensions)
                          .Should().BeEmpty();
        }

        [TestCase("Album/file.foo.bar", true)]
        [TestCase("Album/file.bar", false)]
        public void should_match_a_compound_user_extension_on_the_file_name(string fileName, bool matches)
        {
            FileExtensions.FindUnsafeExtensions(new[] { fileName }, ".foo.bar")
                          .ContainsKey(FailDownloads.UserDefinedExtensions).Should().Be(matches);
        }

        [Test]
        public void should_trim_whitespace_and_skip_empty_extensions()
        {
            FileExtensions.ParseExtensions("nfo,\nxyz, .TXT ,, . ,\t")
                          .Should().Equal(".nfo", ".xyz", ".TXT");
        }
    }
}
