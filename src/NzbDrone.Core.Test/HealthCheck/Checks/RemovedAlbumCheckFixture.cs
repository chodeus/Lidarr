using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.HealthCheck;
using NzbDrone.Core.HealthCheck.Checks;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Music;
using NzbDrone.Core.Music.Events;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.HealthCheck.Checks
{
    [TestFixture]
    public class RemovedAlbumCheckFixture : CoreTest<RemovedAlbumCheck>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<ILocalizationService>()
                  .Setup(s => s.GetLocalizedString(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
                  .Returns<string, Dictionary<string, object>>((phrase, tokens) => $"{phrase}: {tokens.Select(t => $"{t.Key}={t.Value}").Join(" | ")}");
        }

        private void GivenMissingAlbums(params string[] artists)
        {
            Mocker.GetMock<IAlbumService>()
                  .Setup(s => s.GetAlbumsMissingFromMetadataWithoutFiles())
                  .Returns(artists.Select(artist => new Album
                  {
                      Title = "Album Title",
                      ArtistMetadata = artist == null ? null : new ArtistMetadata { Name = artist }
                  }).ToList());
        }

        [Test]
        public void should_check_again_when_an_album_is_updated()
        {
            typeof(RemovedAlbumCheck).GetCustomAttributes(typeof(CheckOnAttribute), true)
                .Cast<CheckOnAttribute>()
                .Should().Contain(a => a.EventType == typeof(AlbumUpdatedEvent));
        }

        [Test]
        public void should_be_ok_when_no_album_is_missing()
        {
            GivenMissingAlbums();

            Subject.Check().ShouldBeOk();
        }

        [Test]
        public void should_name_a_single_missing_album_with_its_artist()
        {
            GivenMissingAlbums("Artist");

            var result = Subject.Check();

            result.ShouldBeWarning("RemovedAlbumSingleRemovedHealthCheckMessage: album=Artist - Album Title");
            result.Reason.Should().Be(HealthCheckReason.RemovedAlbumSingle);
        }

        [Test]
        public void should_name_the_album_alone_when_its_artist_is_unknown()
        {
            GivenMissingAlbums((string)null);

            Subject.Check().ShouldBeWarning("RemovedAlbumSingleRemovedHealthCheckMessage: album=Album Title");
        }

        [Test]
        public void should_count_and_name_every_album_sorted_by_artist()
        {
            GivenMissingAlbums("G", "f", "E", "d", "C", "b", "A");

            var result = Subject.Check();

            result.ShouldBeWarning("RemovedAlbumMultipleRemovedHealthCheckMessage: count=7 | albums=A - Album Title, b - Album Title, C - Album Title, d - Album Title, E - Album Title, f - Album Title, G - Album Title");
            result.Reason.Should().Be(HealthCheckReason.RemovedAlbumMultiple);
        }

        [TestCase(1)]
        [TestCase(2)]
        public void should_link_to_what_to_do_with_these_albums(int albums)
        {
            GivenMissingAlbums(Enumerable.Repeat("Artist", albums).ToArray());

            Subject.Check().WikiUrl.FullUri.Should().Be("https://github.com/chodeus/Lidarr/blob/fix/keep-unlisted-metadata/.github/README.md#what-to-do-with-an-album-in-the-warning");
        }
    }
}
