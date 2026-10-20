using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download.Pending;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Jobs;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Delay;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download.Pending.PendingReleaseServiceTests
{
    // The real DelayProfileService ranks the profiles; the repository behind it is mocked
    [TestFixture]
    public class PendingQueueDelayFixture : CoreTest<PendingReleaseService>
    {
        private const int TaggedDelay = 120;

        [SetUp]
        public void Setup()
        {
            var artist = new Artist { Id = 10, Tags = new HashSet<int> { 5 }, QualityProfile = new QualityProfile { Items = Qualities.QualityFixture.GetDefaultQualities() } };

            Mocker.GetMock<IPendingReleaseRepository>()
                  .Setup(r => r.WithoutFallback())
                  .Returns(new List<PendingRelease>
                  {
                      new PendingRelease
                      {
                          Id = 1,
                          ArtistId = artist.Id,
                          Title = "Artist.Title-Album.Title-WEB-FLAC-2026-GROUP",
                          Added = DateTime.UtcNow,
                          ParsedAlbumInfo = new ParsedAlbumInfo { Quality = new QualityModel(Quality.FLAC) },
                          Release = new ReleaseInfo { Title = "Artist.Title-Album.Title-WEB-FLAC-2026-GROUP", PublishDate = DateTime.UtcNow, DownloadProtocol = nameof(TorrentDownloadProtocol) }
                      }
                  });

            Mocker.GetMock<IArtistService>()
                  .Setup(s => s.GetArtists(It.IsAny<IEnumerable<int>>()))
                  .Returns(new List<Artist> { artist });

            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.GetAlbums(It.IsAny<ParsedAlbumInfo>(), It.IsAny<Artist>(), It.IsAny<SearchCriteriaBase>()))
                  .Returns(new List<Album> { new Album { Id = 20 } });

            Mocker.GetMock<ITaskManager>()
                  .Setup(s => s.GetNextExecution(It.IsAny<Type>()))
                  .Returns(DateTime.UtcNow);

            Mocker.GetMock<IConfigService>()
                  .SetupGet(s => s.RssSyncInterval)
                  .Returns(15);
        }

        private void GivenDelayProfiles(params DelayProfile[] profiles)
        {
            var repo = new Mock<IDelayProfileRepository>();
            repo.Setup(r => r.All()).Returns(profiles);

            Mocker.SetConstant<IDelayProfileService>(new DelayProfileService(repo.Object, new IDownloadProtocol[] { new UsenetDownloadProtocol(), new TorrentDownloadProtocol() }, new CacheManager()));
        }

        private static DelayProfile Profile(int id, int order, int torrentDelay, params int[] tags)
        {
            return new DelayProfile
            {
                Id = id,
                Order = order,
                Tags = new HashSet<int>(tags),
                Items = new List<DelayProfileProtocolItem> { new DelayProfileProtocolItem { Protocol = nameof(TorrentDownloadProtocol), Allowed = true, Delay = torrentDelay } }
            };
        }

        [Test]
        public void should_estimate_a_held_release_from_its_tagged_profile_when_the_default_is_stored_out_of_place()
        {
            GivenDelayProfiles(Profile(1, 0, 0), Profile(2, 1, TaggedDelay, 5));

            var queued = Subject.GetPendingQueue();

            queued.Single().EstimatedCompletionTime.Should().BeAfter(DateTime.UtcNow.AddMinutes(TaggedDelay));
        }
    }
}
