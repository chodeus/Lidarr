using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Blocklisting;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.DecisionEngineTests
{
    [TestFixture]
    public class BlocklistSpecificationFixture : CoreTest<BlocklistSpecification>
    {
        private RemoteAlbum _remoteAlbum;

        [SetUp]
        public void Setup()
        {
            _remoteAlbum = new RemoteAlbum
            {
                Artist = new Artist { Id = 1 },
                Release = new ReleaseInfo { Title = "Artist Name - Album Title (2026) [FLAC]" }
            };
        }

        [Test]
        public void should_reject_a_blocklisted_release_with_the_blocklisted_reason()
        {
            Mocker.GetMock<IBlocklistService>().Setup(s => s.Blocklisted(1, _remoteAlbum.Release)).Returns(true);

            var decision = Subject.IsSatisfiedBy(_remoteAlbum, null);

            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Be(BlocklistSpecification.BlocklistedReason);
        }

        [Test]
        public void should_accept_a_release_that_is_not_blocklisted()
        {
            Subject.IsSatisfiedBy(_remoteAlbum, null).Accepted.Should().BeTrue();
        }
    }
}
