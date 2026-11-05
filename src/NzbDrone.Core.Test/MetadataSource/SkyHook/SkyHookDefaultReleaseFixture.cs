using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MetadataSource.SkyHook;
using NzbDrone.Core.Music;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource.SkyHook
{
    [TestFixture]
    public class SkyHookDefaultReleaseFixture : CoreTest
    {
        private static AlbumRelease Release(string id, string status, int trackCount)
        {
            return new AlbumRelease { ForeignReleaseId = id, Status = status, TrackCount = trackCount };
        }

        [Test]
        public void should_prefer_an_official_release_over_a_withdrawn_one_of_the_same_length()
        {
            var releases = new List<AlbumRelease> { Release("withdrawn", "Withdrawn", 1), Release("official", "Official", 1) };

            SkyHookProxy.DefaultRelease(releases).ForeignReleaseId.Should().Be("official");
        }

        [Test]
        public void should_prefer_an_official_release_over_a_longer_bootleg()
        {
            var releases = new List<AlbumRelease> { Release("bootleg", "Bootleg", 12), Release("official", "Official", 10) };

            SkyHookProxy.DefaultRelease(releases).ForeignReleaseId.Should().Be("official");
        }

        [Test]
        public void should_prefer_the_official_release_with_most_tracks()
        {
            var releases = new List<AlbumRelease> { Release("short", "Official", 10), Release("long", "Official", 12) };

            SkyHookProxy.DefaultRelease(releases).ForeignReleaseId.Should().Be("long");
        }

        [Test]
        public void should_take_the_release_with_most_tracks_when_none_is_official()
        {
            var releases = new List<AlbumRelease> { Release("promo", "Promotion", 3), Release("bootleg", "Bootleg", 5) };

            SkyHookProxy.DefaultRelease(releases).ForeignReleaseId.Should().Be("bootleg");
        }
    }
}
