using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using FizzWare.NBuilder;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Http;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Clients.Deluge;
using NzbDrone.Core.Download.Clients.Sabnzbd;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Download
{
    // Real download clients and proxies; only IHttpClient (the network) is mocked
    [TestFixture]
    public class DownloadServiceRealClientFixture : CoreTest<DownloadService>
    {
        private const string Hash = "0123456789abcdef0123456789abcdef01234567";

        private RemoteAlbum _remoteAlbum;
        private List<IDownloadClient> _clients;
        private Mock<IDownloadClient> _second;
        private List<string> _rpcCalls;

        [SetUp]
        public void Setup()
        {
            _clients = new List<IDownloadClient>();
            _rpcCalls = new List<string>();

            Mocker.SetConstant<ICacheManager>(new CacheManager());

            Mocker.GetMock<IProvideDownloadClient>()
                .Setup(v => v.GetDownloadClients(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<HashSet<int>>()))
                .Returns(() => _clients);

            _remoteAlbum = Builder<RemoteAlbum>.CreateNew()
                .With(c => c.Artist = Builder<Artist>.CreateNew().Build())
                .With(c => c.Albums = Builder<Album>.CreateListOfSize(1).Build().ToList())
                .Build();
        }

        private void WithSecondClient(string protocol)
        {
            _second = new Mock<IDownloadClient>();
            _second.SetupGet(s => s.Definition).Returns(new DownloadClientDefinition { Id = 2, Name = "Second" });
            _second.SetupGet(s => s.Protocol).Returns(protocol);
            _second.Setup(s => s.Download(It.IsAny<RemoteAlbum>(), It.IsAny<IIndexer>())).ReturnsAsync("second-id");
            _clients.Add(_second.Object);
        }

        private void WithRealDeluge(System.Func<string, int, HttpResponse> respond)
        {
            Mocker.SetConstant<IDelugeProxy>(Mocker.Resolve<DelugeProxy>());

            var deluge = Mocker.Resolve<Deluge>();
            deluge.Definition = new DownloadClientDefinition
            {
                Id = 1,
                Name = "Deluge",
                Settings = new DelugeSettings { Host = "deluge", Port = 8112, Password = "pw", MusicCategory = "lidarr" }
            };

            _clients.Insert(0, deluge);

            Mocker.GetMock<IHttpClient>()
                .Setup(s => s.Execute(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(r =>
                {
                    var method = r.ContentSummary.Split('(')[0];
                    _rpcCalls.Add(method);
                    var response = respond(method, _rpcCalls.Count(c => c == method));
                    response = new HttpResponse(r, response.Headers, response.Content, response.StatusCode);

                    if ((int)response.StatusCode >= 400)
                    {
                        throw new HttpException(r, response);
                    }

                    return response;
                });

            _remoteAlbum.Release = new TorrentInfo
            {
                Title = "Artist - Album",
                DownloadProtocol = nameof(TorrentDownloadProtocol),
                DownloadUrl = $"magnet:?xt=urn:btih:{Hash}&tr=udp://tracker.example:80",
                InfoHash = Hash
            };
        }

        private static HttpResponse Json(string json, HttpStatusCode status = HttpStatusCode.OK)
        {
            return new HttpResponse(new HttpRequest("http://deluge"), new HttpHeader(), json, status);
        }

        [Test]
        public async Task deluge_timeout_on_add_should_not_send_release_to_another_client()
        {
            WithRealDeluge((method, n) => method switch
            {
                "auth.login" => Json("{\"result\":true,\"id\":1}"),
                "web.connected" => Json("{\"result\":true,\"id\":1}"),
                "core.add_torrent_magnet" when n == 1 => throw new HttpException(new HttpRequest("http://deluge"), Json("", HttpStatusCode.RequestTimeout)),
                "core.add_torrent_magnet" => Json("{\"result\":null,\"error\":{\"message\":\"Torrent already in session\",\"code\":4},\"id\":1}"),
                _ => Json("{\"result\":null,\"id\":1}")
            });
            WithSecondClient(nameof(TorrentDownloadProtocol));

            var outcome = "grabbed";
            try
            {
                await Subject.DownloadReport(_remoteAlbum, null);
            }
            catch (System.Exception ex)
            {
                outcome = ex.GetType().Name + ": " + ex.Message;
            }

            Assert.That(_second.Invocations.Count(i => i.Method.Name == "Download"), Is.EqualTo(0), $"Second client also got the release. Outcome: {outcome}. Deluge RPC calls: {string.Join(", ", _rpcCalls)}");
        }

        [Test]
        public async Task deluge_session_expiry_after_add_should_not_send_release_to_another_client()
        {
            WithRealDeluge((method, n) => method switch
            {
                "auth.login" when n == 1 => Json("{\"result\":true,\"id\":1}"),
                "auth.login" => Json("{\"result\":false,\"id\":1}"),
                "web.connected" => Json("{\"result\":true,\"id\":1}"),
                "core.add_torrent_magnet" => Json($"{{\"result\":\"{Hash}\",\"id\":1}}"),
                "label.set_torrent" => Json("{\"result\":null,\"error\":{\"message\":\"Not authenticated\",\"code\":1},\"id\":1}"),
                _ => Json("{\"result\":null,\"id\":1}")
            });
            WithSecondClient(nameof(TorrentDownloadProtocol));

            var outcome = "grabbed";
            try
            {
                await Subject.DownloadReport(_remoteAlbum, null);
            }
            catch (System.Exception ex)
            {
                outcome = ex.GetType().Name + ": " + ex.Message;
            }

            Assert.That(_second.Invocations.Count(i => i.Method.Name == "Download"), Is.EqualTo(0), $"Second client also got the release. Outcome: {outcome}. Deluge RPC calls: {string.Join(", ", _rpcCalls)}");
        }

        [Test]
        public async Task unreachable_indexer_should_not_fall_back_and_should_back_off_the_indexer()
        {
            var sab = Mocker.Resolve<Sabnzbd>();
            sab.Definition = new DownloadClientDefinition { Id = 1, Name = "SABnzbd", Settings = new SabnzbdSettings { Host = "sab", Port = 8080, ApiKey = "key" } };
            _clients.Add(sab);
            WithSecondClient(nameof(UsenetDownloadProtocol));

            _remoteAlbum.Release = new ReleaseInfo
            {
                Title = "Artist - Album",
                DownloadProtocol = nameof(UsenetDownloadProtocol),
                DownloadUrl = "http://prowlarr:9696/1/download?link=abc",
                IndexerId = 5
            };

            // SocketsHttpHandler throws this through ManagedHttpDispatcher unwrapped
            Mocker.GetMock<IHttpClient>()
                .Setup(s => s.GetAsync(It.IsAny<HttpRequest>()))
                .ThrowsAsync(new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused (prowlarr:9696)"));

            var outcome = "grabbed";
            try
            {
                await Subject.DownloadReport(_remoteAlbum, null);
            }
            catch (System.Exception ex)
            {
                outcome = ex.GetType().Name + ": " + ex.Message;
            }

            Assert.That(_second.Invocations.Count(i => i.Method.Name == "Download"), Is.EqualTo(0), $"Indexer was down, yet the second client was tried. Outcome: {outcome}");
            Mocker.GetMock<IIndexerStatusService>().Verify(v => v.RecordFailure(5, It.IsAny<TimeSpan>()), Times.Once());
        }

        [Test]
        public async Task unreachable_sabnzbd_should_send_release_to_the_next_client()
        {
            Mocker.SetConstant<ISabnzbdProxy>(Mocker.Resolve<SabnzbdProxy>());

            var sab = Mocker.Resolve<Sabnzbd>();
            sab.Definition = new DownloadClientDefinition { Id = 1, Name = "SABnzbd", Settings = new SabnzbdSettings { Host = "sab", Port = 8080, ApiKey = "key" } };
            _clients.Add(sab);
            WithSecondClient(nameof(UsenetDownloadProtocol));

            _remoteAlbum.Release = new ReleaseInfo
            {
                Title = "Artist - Album",
                DownloadProtocol = nameof(UsenetDownloadProtocol),
                DownloadUrl = "http://indexer.example/download?id=1",
                IndexerId = 5
            };

            Mocker.GetMock<IHttpClient>()
                .Setup(s => s.GetAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(new HttpResponse(new HttpRequest("http://indexer.example"), new HttpHeader(), new byte[] { 1 }));
            Mocker.GetMock<IHttpClient>()
                .Setup(s => s.Execute(It.IsAny<HttpRequest>()))
                .Throws(new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused (sab:8080)"));

            await Subject.DownloadReport(_remoteAlbum, null);

            Assert.That(_second.Invocations.Count(i => i.Method.Name == "Download"), Is.EqualTo(1));
            ExceptionVerification.ExpectedWarns(1);
        }
    }
}
