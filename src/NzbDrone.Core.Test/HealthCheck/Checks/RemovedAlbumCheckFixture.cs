using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NLog;
using NLog.Config;
using NLog.Targets;
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
                  .Setup(s => s.GetAlbumsMissingFromMetadata())
                  .Returns(artists.Select((artist, i) => new Album
                  {
                      Title = "Album Title",
                      ForeignAlbumId = $"album-{i + 1}",
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
        public void should_count_and_name_up_to_five_albums_by_artist()
        {
            GivenMissingAlbums("E", "d", "C", "b", "A");

            var result = Subject.Check();

            result.ShouldBeWarning("RemovedAlbumMultipleRemovedHealthCheckMessage: count=5 | albums=A - Album Title, b - Album Title, C - Album Title, d - Album Title, E - Album Title");
            result.Reason.Should().Be(HealthCheckReason.RemovedAlbumMultiple);
        }

        [Test]
        public void should_count_the_albums_after_the_first_five()
        {
            GivenMissingAlbums("G", "F", "E", "D", "C", "B", "A");

            Subject.Check().ShouldBeWarning("RemovedAlbumMultipleRemovedHealthCheckMessage: count=7 | albums=RemovedAlbumMoreHealthCheckMessage: albums=A - Album Title, B - Album Title, C - Album Title, D - Album Title, E - Album Title | count=2");
        }

        [TestCase(1)]
        [TestCase(2)]
        public void should_link_to_what_to_do_with_a_kept_album(int albums)
        {
            GivenMissingAlbums(Enumerable.Repeat("Artist", albums).ToArray());

            Subject.Check().WikiUrl.FullUri.Should().Be("https://github.com/chodeus/Lidarr/blob/fix/keep-unlisted-metadata/.github/README.md#what-to-do-with-a-kept-album");
        }

        [Test]
        public void should_log_the_whole_list_once_each_time_it_changes()
        {
            var logs = new MemoryTarget { Layout = "${message}" };
            var config = new LoggingConfiguration();
            config.AddRuleForAllLevels(logs);
            Mocker.SetConstant(new LogFactory { Configuration = config }.GetLogger(nameof(RemovedAlbumCheck)));

            GivenMissingAlbums("A", "B", "C", "D", "E", "F");
            Subject.Check();
            Subject.Check();

            logs.Logs.Should().ContainSingle().Which.Should().Contain("F - Album Title (mbid album-6)");

            GivenMissingAlbums("A", "B");
            Subject.Check();

            logs.Logs.Should().HaveCount(2);
            logs.Logs.Last().Should().EndWith("A - Album Title (mbid album-1), B - Album Title (mbid album-2)");

            GivenMissingAlbums();
            Subject.Check();
            GivenMissingAlbums("A", "B");
            Subject.Check();

            logs.Logs.Should().HaveCount(3);
        }
    }
}
