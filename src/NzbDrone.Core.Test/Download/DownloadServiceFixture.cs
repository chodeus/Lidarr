using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using FizzWare.NBuilder;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Clients;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Download
{
    [TestFixture]
    public class DownloadServiceFixture : CoreTest<DownloadService>
    {
        private RemoteAlbum _parseResult;
        private List<IDownloadClient> _downloadClients;
        [SetUp]
        public void Setup()
        {
            _downloadClients = new List<IDownloadClient>();

            Mocker.GetMock<IProvideDownloadClient>()
                .Setup(v => v.GetDownloadClients(It.IsAny<bool>()))
                .Returns(_downloadClients);

            Mocker.GetMock<IProvideDownloadClient>()
                .Setup(v => v.GetDownloadClients(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<HashSet<int>>()))
                .Returns<string, int, bool, HashSet<int>>((v, i, f, t) => _downloadClients.Where(d => d.Protocol == v));

            var episodes = Builder<Album>.CreateListOfSize(2)
                .TheFirst(1).With(s => s.Id = 12)
                .TheNext(1).With(s => s.Id = 99)
                .All().With(s => s.ArtistId = 5)
                .Build().ToList();

            var releaseInfo = Builder<ReleaseInfo>.CreateNew()
                .With(v => v.DownloadProtocol = nameof(UsenetDownloadProtocol))
                .With(v => v.DownloadUrl = "http://test.site/download1.ext")
                .Build();

            _parseResult = Builder<RemoteAlbum>.CreateNew()
                   .With(c => c.Artist = Builder<Artist>.CreateNew().Build())
                   .With(c => c.Release = releaseInfo)
                   .With(c => c.Albums = episodes)
                   .Build();
        }

        private Mock<IDownloadClient> WithUsenetClient()
        {
            var mock = new Mock<IDownloadClient>(MockBehavior.Default);
            mock.SetupGet(s => s.Definition).Returns(Builder<IndexerDefinition>.CreateNew().Build());

            _downloadClients.Add(mock.Object);

            mock.SetupGet(v => v.Protocol).Returns(nameof(UsenetDownloadProtocol));
            mock.SetupGet(v => v.CanFallBackFrom).Returns(true);

            return mock;
        }

        private Mock<IDownloadClient> WithTorrentClient()
        {
            var mock = new Mock<IDownloadClient>(MockBehavior.Default);
            mock.SetupGet(s => s.Definition).Returns(Builder<IndexerDefinition>.CreateNew().Build());

            _downloadClients.Add(mock.Object);

            mock.SetupGet(v => v.Protocol).Returns(nameof(TorrentDownloadProtocol));
            mock.SetupGet(v => v.CanFallBackFrom).Returns(true);

            return mock;
        }

        [Test]
        public async Task Download_report_should_publish_on_grab_event()
        {
            var mock = WithUsenetClient();
            mock.Setup(s => s.Download(It.IsAny<RemoteAlbum>(), It.IsAny<IIndexer>()));

            await Subject.DownloadReport(_parseResult, null);

            VerifyEventPublished<AlbumGrabbedEvent>();
        }

        [Test]
        public async Task Download_report_should_grab_using_client()
        {
            var mock = WithUsenetClient();
            mock.Setup(s => s.Download(It.IsAny<RemoteAlbum>(), It.IsAny<IIndexer>()));

            await Subject.DownloadReport(_parseResult, null);

            mock.Verify(s => s.Download(It.IsAny<RemoteAlbum>(), It.IsAny<IIndexer>()), Times.Once());
        }

        [Test]
        public void Download_report_should_not_publish_on_failed_grab_event()
        {
            var mock = WithUsenetClient();
            mock.Setup(s => s.Download(It.IsAny<RemoteAlbum>(), It.IsAny<IIndexer>()))
                .Throws(new WebException());

            Assert.ThrowsAsync<WebException>(async () => await Subject.DownloadReport(_parseResult, null));

            VerifyEventNotPublished<AlbumGrabbedEvent>();
        }

        [Test]
        public void Download_report_should_trigger_indexer_backoff_on_indexer_error()
        {
            var mock = WithUsenetClient();
            mock.Setup(s => s.Download(It.IsAny<RemoteAlbum>(), It.IsAny<IIndexer>()))
                .Callback<RemoteAlbum, IIndexer>((v, indexer) =>
                {
                    throw new ReleaseDownloadException(v.Release, "Error", new WebException());
                });

            Assert.ThrowsAsync<ReleaseDownloadException>(async () => await Subject.DownloadReport(_parseResult, null));

            Mocker.GetMock<IIndexerStatusService>()
                .Verify(v => v.RecordFailure(It.IsAny<int>(), It.IsAny<TimeSpan>()), Times.Once());
        }

        [Test]
        public void Download_report_should_trigger_indexer_backoff_on_http429_with_long_time()
        {
            var request = new HttpRequest("http://my.indexer.com");
            var response = new HttpResponse(request, new HttpHeader(), Array.Empty<byte>(), (HttpStatusCode)429);
            response.Headers["Retry-After"] = "300";

            var mock = WithUsenetClient();
            mock.Setup(s => s.Download(It.IsAny<RemoteAlbum>(), It.IsAny<IIndexer>()))
                .Callback<RemoteAlbum, IIndexer>((v, indexer) =>
                {
                    throw new ReleaseDownloadException(v.Release, "Error", new TooManyRequestsException(request, response));
                });

            Assert.ThrowsAsync<ReleaseDownloadException>(async () => await Subject.DownloadReport(_parseResult, null));

            Mocker.GetMock<IIndexerStatusService>()
                .Verify(v => v.RecordFailure(It.IsAny<int>(), TimeSpan.FromMinutes(5.0)), Times.Once());
        }

        [Test]
        public void Download_report_should_trigger_indexer_backoff_on_http429_based_on_date()
        {
            var request = new HttpRequest("http://my.indexer.com");
            var response = new HttpResponse(request, new HttpHeader(), Array.Empty<byte>(), (HttpStatusCode)429);
            response.Headers["Retry-After"] = DateTime.UtcNow.AddSeconds(300).ToString("r");

            var mock = WithUsenetClient();
            mock.Setup(s => s.Download(It.IsAny<RemoteAlbum>(), It.IsAny<IIndexer>()))
                .Callback<RemoteAlbum, IIndexer>((v, indexer) =>
                {
                    throw new ReleaseDownloadException(v.Release, "Error", new TooManyRequestsException(request, response));
                });

            Assert.ThrowsAsync<ReleaseDownloadException>(async () => await Subject.DownloadReport(_parseResult, null));

            Mocker.GetMock<IIndexerStatusService>()
                .Verify(v => v.RecordFailure(It.IsAny<int>(),
                    It.IsInRange<TimeSpan>(TimeSpan.FromMinutes(4.9), TimeSpan.FromMinutes(5.1), Moq.Range.Inclusive)),
                    Times.Once());
        }

        [Test]
        public void Download_report_should_not_trigger_indexer_backoff_on_downloadclient_error()
        {
            var mock = WithUsenetClient();
            mock.Setup(s => s.Download(It.IsAny<RemoteAlbum>(), It.IsAny<IIndexer>()))
                .Throws(new DownloadClientException("Some Error"));

            Assert.ThrowsAsync<DownloadClientException>(async () => await Subject.DownloadReport(_parseResult, null));

            Mocker.GetMock<IIndexerStatusService>()
                .Verify(v => v.RecordFailure(It.IsAny<int>(), It.IsAny<TimeSpan>()), Times.Never());
        }

        [Test]
        public void Download_report_should_not_trigger_indexer_backoff_on_indexer_404_error()
        {
            var mock = WithUsenetClient();
            mock.Setup(s => s.Download(It.IsAny<RemoteAlbum>(), It.IsAny<IIndexer>()))
                .Callback<RemoteAlbum, IIndexer>((v, indexer) =>
                {
                    throw new ReleaseUnavailableException(v.Release, "Error", new WebException());
                });

            Assert.ThrowsAsync<ReleaseUnavailableException>(async () => await Subject.DownloadReport(_parseResult, null));

            Mocker.GetMock<IIndexerStatusService>()
                .Verify(v => v.RecordFailure(It.IsAny<int>(), It.IsAny<TimeSpan>()), Times.Never());
        }

        [Test]
        public void should_not_attempt_download_if_client_isnt_configured()
        {
            Assert.ThrowsAsync<DownloadClientUnavailableException>(async () => await Subject.DownloadReport(_parseResult, null));

            Mocker.GetMock<IDownloadClient>().Verify(c => c.Download(It.IsAny<RemoteAlbum>(), It.IsAny<IIndexer>()), Times.Never());
            VerifyEventNotPublished<AlbumGrabbedEvent>();
        }

        [Test]
        public async Task should_attempt_download_even_if_client_is_disabled()
        {
            var mockUsenet = WithUsenetClient();

            Mocker.GetMock<IDownloadClientStatusService>()
                    .Setup(v => v.GetBlockedProviders())
                    .Returns(new List<DownloadClientStatus>
                    {
                        new DownloadClientStatus
                        {
                            ProviderId = _downloadClients.First().Definition.Id,
                            DisabledTill = DateTime.UtcNow.AddHours(3)
                        }
                    });

            await Subject.DownloadReport(_parseResult, null);

            Mocker.GetMock<IDownloadClientStatusService>().Verify(c => c.GetBlockedProviders(), Times.Never());
            mockUsenet.Verify(c => c.Download(It.IsAny<RemoteAlbum>(), It.IsAny<IIndexer>()), Times.Once());
            VerifyEventPublished<AlbumGrabbedEvent>();
        }

        [Test]
        public async Task should_send_download_to_correct_usenet_client()
        {
            var mockTorrent = WithTorrentClient();
            var mockUsenet = WithUsenetClient();

            await Subject.DownloadReport(_parseResult, null);

            mockTorrent.Verify(c => c.Download(It.IsAny<RemoteAlbum>(), It.IsAny<IIndexer>()), Times.Never());
            mockUsenet.Verify(c => c.Download(It.IsAny<RemoteAlbum>(), It.IsAny<IIndexer>()), Times.Once());
        }

        private Mock<IDownloadClient> WithNamedUsenetClient(int id)
        {
            var mock = new Mock<IDownloadClient>(MockBehavior.Default);
            mock.SetupGet(s => s.Definition).Returns(new DownloadClientDefinition { Id = id, Name = "Client " + id });
            mock.SetupGet(v => v.Protocol).Returns(nameof(UsenetDownloadProtocol));
            mock.SetupGet(v => v.CanFallBackFrom).Returns(true);

            _downloadClients.Add(mock.Object);

            return mock;
        }

        private static Exception Unreachable(string client)
        {
            return new DownloadClientUnavailableException($"Unable to connect to {client}", new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused"));
        }

        private static IEnumerable<Exception> ClientNeverTookIt()
        {
            yield return Unreachable("Client 1");
            yield return new DownloadClientException("Unable to connect to Client 1", new HttpRequestException(HttpRequestError.NameResolutionError, "Unknown host"));
            yield return new DownloadClientUnavailableException("Unable to connect to Client 1", new HttpRequestException(HttpRequestError.SecureConnectionError, "TLS handshake failed"));
            yield return new DownloadClientUnavailableException("Unable to connect to Client 1", new HttpRequestException(HttpRequestError.ProxyTunnelError, "Proxy tunnel failed"));
            yield return new DownloadClientAuthenticationException("Failed to authenticate with Client 1");
        }

        private static IEnumerable<Exception> ClientMayHaveTakenIt()
        {
            yield return new DownloadClientUnavailableException("Unable to connect to Client 1", new WebException("Http request timed out", WebExceptionStatus.Timeout));
            yield return new DownloadClientException("Unable to connect to Client 1", new HttpRequestException(HttpRequestError.ResponseEnded, "Response ended"));
            yield return new DownloadClientException("Error response received from Client 1");
        }

        [TestCaseSource(nameof(ClientNeverTookIt))]
        public async Task should_fall_back_to_the_next_client_when_one_could_not_take_it(Exception failure)
        {
            var first = WithNamedUsenetClient(1);
            var second = WithNamedUsenetClient(2);
            first.Setup(s => s.Download(It.IsAny<RemoteAlbum>(), It.IsAny<IIndexer>())).Throws(failure);
            second.Setup(s => s.Download(It.IsAny<RemoteAlbum>(), It.IsAny<IIndexer>())).ReturnsAsync("download-2");

            await Subject.DownloadReport(_parseResult, null);

            first.Verify(c => c.Download(It.IsAny<RemoteAlbum>(), It.IsAny<IIndexer>()), Times.Once());
            second.Verify(c => c.Download(It.IsAny<RemoteAlbum>(), It.IsAny<IIndexer>()), Times.Once());
            Mocker.GetMock<IProvideDownloadClient>().Verify(v => v.ReportSuccessfulDownloadClient(nameof(UsenetDownloadProtocol), 2), Times.Once());
            Mocker.GetMock<IDownloadClientStatusService>().Verify(v => v.RecordSuccess(2), Times.Once());
            VerifyEventPublished<AlbumGrabbedEvent>();
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_not_fall_back_from_a_client_whose_add_can_partly_succeed()
        {
            var first = WithNamedUsenetClient(1);
            var second = WithNamedUsenetClient(2);
            first.SetupGet(v => v.CanFallBackFrom).Returns(false);
            first.Setup(s => s.Download(It.IsAny<RemoteAlbum>(), It.IsAny<IIndexer>())).Throws(Unreachable("Client 1"));

            Assert.ThrowsAsync<DownloadClientUnavailableException>(async () => await Subject.DownloadReport(_parseResult, null));

            second.Verify(c => c.Download(It.IsAny<RemoteAlbum>(), It.IsAny<IIndexer>()), Times.Never());
        }

        [Test]
        public void should_not_fall_back_when_the_release_itself_fails()
        {
            var first = WithNamedUsenetClient(1);
            var second = WithNamedUsenetClient(2);
            first.Setup(s => s.Download(It.IsAny<RemoteAlbum>(), It.IsAny<IIndexer>())).Throws(new ReleaseUnavailableException(_parseResult.Release, "Gone"));

            Assert.ThrowsAsync<ReleaseUnavailableException>(async () => await Subject.DownloadReport(_parseResult, null));

            second.Verify(c => c.Download(It.IsAny<RemoteAlbum>(), It.IsAny<IIndexer>()), Times.Never());
        }

        [TestCaseSource(nameof(ClientMayHaveTakenIt))]
        public void should_not_fall_back_when_the_client_may_have_taken_it(Exception failure)
        {
            var first = WithNamedUsenetClient(1);
            var second = WithNamedUsenetClient(2);
            first.Setup(s => s.Download(It.IsAny<RemoteAlbum>(), It.IsAny<IIndexer>())).Throws(failure);

            Assert.ThrowsAsync(failure.GetType(), async () => await Subject.DownloadReport(_parseResult, null));

            second.Verify(c => c.Download(It.IsAny<RemoteAlbum>(), It.IsAny<IIndexer>()), Times.Never());
            VerifyEventNotPublished<AlbumGrabbedEvent>();
        }

        [Test]
        public void should_not_fall_back_when_the_indexer_cannot_be_reached()
        {
            var first = WithNamedUsenetClient(1);
            var second = WithNamedUsenetClient(2);
            first.Setup(s => s.Download(It.IsAny<RemoteAlbum>(), It.IsAny<IIndexer>()))
                 .Throws(new ReleaseDownloadException(_parseResult.Release, "Unable to fetch the release", new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused")));

            Assert.ThrowsAsync<ReleaseDownloadException>(async () => await Subject.DownloadReport(_parseResult, null));

            second.Verify(c => c.Download(It.IsAny<RemoteAlbum>(), It.IsAny<IIndexer>()), Times.Never());
        }

        [Test]
        public void should_report_the_last_clients_error_when_every_client_fails()
        {
            WithNamedUsenetClient(1).Setup(s => s.Download(It.IsAny<RemoteAlbum>(), It.IsAny<IIndexer>())).Throws(Unreachable("Client 1"));
            WithNamedUsenetClient(2).Setup(s => s.Download(It.IsAny<RemoteAlbum>(), It.IsAny<IIndexer>())).Throws(new WebException("Client 2 is down"));

            Assert.ThrowsAsync<WebException>(async () => await Subject.DownloadReport(_parseResult, null));

            Mocker.GetMock<IProvideDownloadClient>().Verify(v => v.ReportSuccessfulDownloadClient(It.IsAny<string>(), It.IsAny<int>()), Times.Never());
            VerifyEventNotPublished<AlbumGrabbedEvent>();
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_not_send_to_another_client_once_one_took_the_release()
        {
            var first = WithNamedUsenetClient(1);
            var second = WithNamedUsenetClient(2);
            first.Setup(s => s.Download(It.IsAny<RemoteAlbum>(), It.IsAny<IIndexer>())).ReturnsAsync("download-1");
            Mocker.GetMock<IDownloadClientStatusService>().Setup(v => v.RecordSuccess(1)).Throws(new InvalidOperationException("Database is locked"));

            Assert.ThrowsAsync<InvalidOperationException>(async () => await Subject.DownloadReport(_parseResult, null));

            second.Verify(c => c.Download(It.IsAny<RemoteAlbum>(), It.IsAny<IIndexer>()), Times.Never());
        }

        [Test]
        public void should_only_use_the_chosen_client()
        {
            var first = WithNamedUsenetClient(1);
            var second = WithNamedUsenetClient(2);
            first.Setup(s => s.Download(It.IsAny<RemoteAlbum>(), It.IsAny<IIndexer>())).Throws(Unreachable("Client 1"));
            Mocker.GetMock<IProvideDownloadClient>().Setup(v => v.Get(1)).Returns(first.Object);

            Assert.ThrowsAsync<DownloadClientUnavailableException>(async () => await Subject.DownloadReport(_parseResult, 1));

            second.Verify(c => c.Download(It.IsAny<RemoteAlbum>(), It.IsAny<IIndexer>()), Times.Never());
        }

        [Test]
        public async Task should_send_download_to_correct_torrent_client()
        {
            var mockTorrent = WithTorrentClient();
            var mockUsenet = WithUsenetClient();

            _parseResult.Release.DownloadProtocol = nameof(TorrentDownloadProtocol);

            await Subject.DownloadReport(_parseResult, null);

            mockTorrent.Verify(c => c.Download(It.IsAny<RemoteAlbum>(), It.IsAny<IIndexer>()), Times.Once());
            mockUsenet.Verify(c => c.Download(It.IsAny<RemoteAlbum>(), It.IsAny<IIndexer>()), Times.Never());
        }
    }
}
