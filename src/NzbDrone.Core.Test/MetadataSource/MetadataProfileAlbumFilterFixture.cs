using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Music;
using NzbDrone.Core.Profiles.Metadata;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource
{
    [TestFixture]
    public class MetadataProfileAlbumFilterFixture : CoreTest
    {
        public static MetadataProfile AlbumsOnlyProfile()
        {
            return new MetadataProfile
            {
                PrimaryAlbumTypes = new List<ProfilePrimaryAlbumTypeItem>
                {
                    new() { PrimaryAlbumType = PrimaryAlbumType.Album, Allowed = true },
                    new() { PrimaryAlbumType = PrimaryAlbumType.Single, Allowed = false }
                },
                SecondaryAlbumTypes = new List<ProfileSecondaryAlbumTypeItem>
                {
                    new() { SecondaryAlbumType = SecondaryAlbumType.Studio, Allowed = true },
                    new() { SecondaryAlbumType = SecondaryAlbumType.Live, Allowed = true },
                    new() { SecondaryAlbumType = SecondaryAlbumType.Compilation, Allowed = false }
                },
                ReleaseStatuses = new List<ProfileReleaseStatusItem>
                {
                    new() { ReleaseStatus = ReleaseStatus.Official, Allowed = true },
                    new() { ReleaseStatus = ReleaseStatus.Bootleg, Allowed = false }
                }
            };
        }

        [TestCase("Album", new string[0], "Official", true)]
        [TestCase("Single", new string[0], "Official", false)]
        [TestCase("Album", new[] { "Live" }, "Official", true)]
        [TestCase("Album", new[] { "Compilation" }, "Official", false)]
        [TestCase("Album", new string[0], "Bootleg", false)]
        public void allows_only_what_the_profile_allows(string primaryType, string[] secondaryTypes, string releaseStatus, bool expected)
        {
            new MetadataProfileAlbumFilter(AlbumsOnlyProfile())
                .Allows(primaryType, secondaryTypes, new[] { releaseStatus })
                .Should().Be(expected);
        }
    }
}
