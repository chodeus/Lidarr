using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download
{
    // The real IgnoredDownloadService and history services over the test database
    [TestFixture]
    public class IgnoredUnmatchedDownloadHistoryFixture : DbTest<DownloadHistoryRepository, DownloadHistory>
    {
        private DownloadHistoryService _downloadHistory;

        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<IDownloadHistoryRepository>(Subject);
            Mocker.SetConstant<IHistoryRepository>(Mocker.Resolve<EntityHistoryRepository>());
            _downloadHistory = Mocker.Resolve<DownloadHistoryService>();
            var entityHistory = Mocker.Resolve<EntityHistoryService>();

            Mocker.GetMock<IEventAggregator>()
                  .Setup(v => v.PublishEvent(It.IsAny<DownloadIgnoredEvent>()))
                  .Callback<DownloadIgnoredEvent>(e =>
                  {
                      _downloadHistory.Handle(e);
                      entityHistory.Handle(e);
                  });
        }

        [Test]
        public void should_store_the_ignore_of_an_unmatched_download_by_its_download_id()
        {
            var trackedDownload = new TrackedDownload
            {
                DownloadItem = new DownloadClientItem
                {
                    Title = "Ambiguous Artist - Album",
                    DownloadId = "unmatched-id",
                    DownloadClientInfo = new DownloadClientItemClientInfo { Protocol = nameof(TorrentDownloadProtocol), Id = 1, Name = "Client", Type = "QBittorrent" }
                }
            };

            Mocker.Resolve<IgnoredDownloadService>().IgnoreDownload(trackedDownload).Should().BeTrue();

            _downloadHistory.GetLatestDownloadHistoryItem("unmatched-id").EventType.Should().Be(DownloadHistoryEventType.DownloadIgnored);
            Db.All<EntityHistory>().Should().BeEmpty();
        }
    }
}
