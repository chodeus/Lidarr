using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
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
                  .Returns<string, Dictionary<string, object>>((phrase, tokens) => $"{phrase}: {tokens.Values.Single()}");
        }

        private void GivenMissingAlbums(params string[] foreignAlbumIds)
        {
            Mocker.GetMock<IAlbumService>()
                  .Setup(s => s.GetAlbumsMissingFromMetadata())
                  .Returns(foreignAlbumIds.Select(id => new Album { Title = "Album Title", ForeignAlbumId = id }).ToList());
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
        public void should_name_a_single_missing_album()
        {
            GivenMissingAlbums("album-1");

            var result = Subject.Check();

            result.ShouldBeWarning("RemovedAlbumSingleRemovedHealthCheckMessage: Album Title (mbid album-1)");
            result.Reason.Should().Be(HealthCheckReason.RemovedAlbumSingle);
        }

        [Test]
        public void should_name_every_missing_album()
        {
            GivenMissingAlbums("album-1", "album-2");

            var result = Subject.Check();

            result.ShouldBeWarning("RemovedAlbumMultipleRemovedHealthCheckMessage: Album Title (mbid album-1), Album Title (mbid album-2)");
            result.Reason.Should().Be(HealthCheckReason.RemovedAlbumMultiple);
        }
    }
}
