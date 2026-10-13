using System;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Music;

namespace NzbDrone.Core.Test.MusicTests
{
    [TestFixture]
    public class BatchLoadingDefaultsFixture
    {
        [TestCase(typeof(IAlbumService), nameof(IAlbumService.LoadArtistsAndReleases))]
        [TestCase(typeof(IAlbumRepository), nameof(IAlbumRepository.LoadArtistsAndReleases))]
        [TestCase(typeof(ITrackService), nameof(ITrackService.LoadReleasesAndArtists))]
        [TestCase(typeof(ITrackRepository), nameof(ITrackRepository.LoadReleasesAndArtists))]
        public void new_interface_members_should_have_default_bodies(Type type, string method)
        {
            type.GetMethod(method).IsAbstract.Should().BeFalse();
        }
    }
}
