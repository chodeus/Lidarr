using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Http;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Clients;
using NzbDrone.Core.Download.Clients.QBittorrent;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download
{
    // Real DownloadService, cache and specification; only the download client is mocked
    [TestFixture]
    public class DuplicateReleaseFixture : CoreTest<DownloadService>
    {
        private const string Title = "Artist.Title-Album.Title-REPACK-WEB-FLAC-2026-GROUP";
        private const string Hash = "CBC2F069FE8BB2F544EAE707D75BCD3DE9DCF951";

        private Mock<IDownloadClient> _downloadClient;
        private bool _stillHeld;

        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<ICacheManager>(new CacheManager());
            Mocker.SetConstant<IDuplicateReleaseCache>(Mocker.Resolve<DuplicateReleaseCache>());

            _downloadClient = new Mock<IDownloadClient>();
            _downloadClient.SetupGet(c => c.Definition).Returns(new DownloadClientDefinition { Id = 1, Name = "qBittorrent" });
            _downloadClient.SetupGet(c => c.Protocol).Returns(nameof(TorrentDownloadProtocol));
            _downloadClient.As<ITorrentPresenceCheck>()
                           .Setup(c => c.HasTorrent(It.IsAny<string>()))
                           .Returns(() => _stillHeld);
            _stillHeld = true;

            Mocker.GetMock<IProvideDownloadClient>()
                  .Setup(p => p.Get(1))
                  .Returns(_downloadClient.Object);

            Mocker.GetMock<IProvideDownloadClient>()
                  .Setup(p => p.GetDownloadClient(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<HashSet<int>>()))
                  .Returns(_downloadClient.Object);
        }

        private static RemoteAlbum RemoteAlbum(int indexerId, string infoHash, string guid = null)
        {
            return new RemoteAlbum
            {
                Artist = new Artist { Id = 5, Tags = new HashSet<int>() },
                Albums = new List<Album> { new Album { Id = 12 } },
                ParsedAlbumInfo = new ParsedAlbumInfo(),
                Release = new TorrentInfo
                {
                    Title = Title,
                    Guid = guid ?? $"indexer{indexerId}-listing1",
                    Indexer = $"Indexer {indexerId}",
                    IndexerId = indexerId,
                    InfoHash = infoHash,
                    DownloadUrl = $"https://indexer{indexerId}.test/download/1",
                    DownloadProtocol = nameof(TorrentDownloadProtocol)
                }
            };
        }

        private static RemoteAlbum UsenetAlbum(string guid)
        {
            var nzb = RemoteAlbum(7, null);
            nzb.Release = new ReleaseInfo { Title = Title, Guid = guid, Indexer = "Indexer 7", IndexerId = 7, DownloadProtocol = nameof(UsenetDownloadProtocol), DownloadUrl = "https://indexer7.test/nzb/1" };

            return nzb;
        }

        private async Task GivenRefusedByTheClient(Exception refusal, RemoteAlbum grabbed = null)
        {
            grabbed ??= RemoteAlbum(7, null);

            _downloadClient.Setup(c => c.Download(It.IsAny<RemoteAlbum>(), It.IsAny<IIndexer>()))
                           .Callback<RemoteAlbum, IIndexer>((r, i) => ((TorrentInfo)r.Release).InfoHash = Hash)
                           .ThrowsAsync(refusal);

            await FluentActions.Awaiting(() => Subject.DownloadReport(grabbed, null)).Should().ThrowAsync<DownloadClientRejectedReleaseException>();
        }

        private Task GivenTheClientAlreadyHasIt()
        {
            return GivenRefusedByTheClient(new ReleaseAlreadyInDownloadClientException(RemoteAlbum(7, null).Release, Hash, "qBittorrent already has this torrent", null));
        }

        private Decision Decide(RemoteAlbum remoteAlbum)
        {
            return Mocker.Resolve<AlreadyInDownloadClientSpecification>().IsSatisfiedBy(remoteAlbum, null);
        }

        [Test]
        public async Task should_reject_the_same_listing_on_a_later_sync()
        {
            await GivenTheClientAlreadyHasIt();

            var decision = Decide(RemoteAlbum(7, null));

            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Be("qBittorrent already has this release");
        }

        [Test]
        public async Task should_reject_a_listing_with_the_same_hash()
        {
            await GivenTheClientAlreadyHasIt();

            Decide(RemoteAlbum(9, Hash.ToLowerInvariant())).Accepted.Should().BeFalse();
        }

        [Test]
        public async Task should_accept_the_same_title_from_another_indexer()
        {
            await GivenTheClientAlreadyHasIt();

            Decide(RemoteAlbum(9, null)).Accepted.Should().BeTrue();
        }

        [Test]
        public async Task should_accept_the_same_title_and_indexer_with_another_hash()
        {
            await GivenTheClientAlreadyHasIt();

            Decide(RemoteAlbum(7, "0000000000000000000000000000000000000000")).Accepted.Should().BeTrue();
        }

        [Test]
        public async Task should_accept_another_listing_with_the_same_title_on_the_same_indexer()
        {
            await GivenTheClientAlreadyHasIt();

            Decide(RemoteAlbum(7, null, "indexer7-listing2")).Accepted.Should().BeTrue();
        }

        [Test]
        public async Task should_tell_pushed_listings_apart_by_guid()
        {
            var pushed = RemoteAlbum(0, null, "PUSH-https://tracker-a.test/download/1");
            await GivenRefusedByTheClient(new ReleaseAlreadyInDownloadClientException(pushed.Release, Hash, "qBittorrent already has this torrent", null), pushed);

            Decide(RemoteAlbum(0, null, "PUSH-https://tracker-a.test/download/1")).Accepted.Should().BeFalse();
            Decide(RemoteAlbum(0, null, "PUSH-https://tracker-b.test/download/1")).Accepted.Should().BeTrue();
        }

        [Test]
        public async Task should_match_a_listing_with_no_guid_by_hash_only()
        {
            var unidentified = RemoteAlbum(0, null, string.Empty);
            await GivenRefusedByTheClient(new ReleaseAlreadyInDownloadClientException(unidentified.Release, Hash, "qBittorrent already has this torrent", null), unidentified);

            Decide(RemoteAlbum(0, null, string.Empty)).Accepted.Should().BeTrue();
            Decide(RemoteAlbum(0, Hash, string.Empty)).Accepted.Should().BeFalse();
        }

        [Test]
        public async Task should_remember_the_hash_the_client_confirmed()
        {
            var grabbed = RemoteAlbum(7, null);

            _downloadClient.Setup(c => c.Download(It.IsAny<RemoteAlbum>(), It.IsAny<IIndexer>()))
                           .ThrowsAsync(new ReleaseAlreadyInDownloadClientException(grabbed.Release, Hash, "qBittorrent already has this torrent", null));

            await FluentActions.Awaiting(() => Subject.DownloadReport(grabbed, null)).Should().ThrowAsync<DownloadClientRejectedReleaseException>();

            Decide(RemoteAlbum(7, null)).Accepted.Should().BeFalse();
            Decide(RemoteAlbum(9, Hash)).Accepted.Should().BeFalse();
        }

        [Test]
        public async Task should_ask_the_client_about_the_refused_torrent()
        {
            await GivenTheClientAlreadyHasIt();

            Decide(RemoteAlbum(7, null));

            _downloadClient.As<ITorrentPresenceCheck>().Verify(c => c.HasTorrent(Hash), Times.Once());
        }

        [Test]
        public async Task should_accept_once_the_client_no_longer_has_the_torrent()
        {
            await GivenTheClientAlreadyHasIt();

            _stillHeld = false;
            Decide(RemoteAlbum(7, null)).Accepted.Should().BeTrue();

            _stillHeld = true;
            Decide(RemoteAlbum(7, null)).Accepted.Should().BeTrue();
        }

        [Test]
        public async Task should_accept_when_the_client_cannot_be_checked()
        {
            await GivenTheClientAlreadyHasIt();

            Mocker.GetMock<IProvideDownloadClient>()
                  .Setup(p => p.Get(1))
                  .Throws(new InvalidOperationException("the download client was removed"));

            Decide(RemoteAlbum(7, null)).Accepted.Should().BeTrue();
        }

        [Test]
        public async Task should_accept_the_same_guid_from_another_indexer()
        {
            await GivenTheClientAlreadyHasIt();

            Decide(RemoteAlbum(9, null, "indexer7-listing1")).Accepted.Should().BeTrue();
        }

        [Test]
        public async Task should_accept_a_usenet_listing_with_the_same_guid()
        {
            await GivenTheClientAlreadyHasIt();

            Decide(UsenetAlbum("indexer7-listing1")).Accepted.Should().BeTrue();
        }

        [Test]
        public async Task should_accept_a_usenet_release_with_the_same_title()
        {
            await GivenTheClientAlreadyHasIt();

            Decide(UsenetAlbum(null)).Accepted.Should().BeTrue();
        }

        [Test]
        public async Task should_not_remember_any_other_rejection()
        {
            await GivenRefusedByTheClient(new DownloadClientRejectedReleaseException(RemoteAlbum(7, null).Release, "qBittorrent rejected the torrent file due to a conflict"));

            Decide(RemoteAlbum(7, null)).Accepted.Should().BeTrue();
        }

        [Test]
        public async Task should_keep_an_entry_a_grab_saved_while_a_recheck_was_asking_the_client()
        {
            await GivenTheClientAlreadyHasIt();

            var checks = 0;
            _downloadClient.As<ITorrentPresenceCheck>()
                           .Setup(c => c.HasTorrent(It.IsAny<string>()))
                           .Returns(() =>
                           {
                               // The torrent is gone when this check asks, then re-added and refused again before it answers
                               if (++checks == 1)
                               {
                                   FluentActions.Awaiting(() => Subject.DownloadReport(RemoteAlbum(7, null), null)).Should().ThrowAsync<DownloadClientRejectedReleaseException>().GetAwaiter().GetResult();
                                   return false;
                               }

                               return true;
                           });

            Decide(RemoteAlbum(7, null)).Accepted.Should().BeTrue();
            Decide(RemoteAlbum(7, null)).Accepted.Should().BeFalse();
        }

        [Test]
        public async Task should_remember_and_recheck_a_conflict_through_the_real_qbittorrent_client()
        {
            var loaded = true;
            var proxy = Mocker.GetMock<IQBittorrentProxy>();
            proxy.Setup(s => s.GetConfig(It.IsAny<QBittorrentSettings>())).Returns(new QBittorrentPreferences { DhtEnabled = true });
            proxy.Setup(s => s.AddTorrentFromUrl(It.IsAny<string>(), It.IsAny<TorrentSeedConfiguration>(), It.IsAny<QBittorrentSettings>()))
                 .Throws(new DownloadClientException("Failed to connect to qBittorrent, check your settings.", new HttpException(new HttpResponse(new HttpRequest("http://127.0.0.1:2222/"), new HttpHeader(), Array.Empty<byte>(), System.Net.HttpStatusCode.Conflict))));
            proxy.Setup(s => s.IsTorrentLoaded(It.IsAny<string>(), It.IsAny<QBittorrentSettings>())).Returns(() => loaded);

            Mocker.GetMock<IQBittorrentProxySelector>().Setup(s => s.GetProxy(It.IsAny<QBittorrentSettings>(), It.IsAny<bool>())).Returns(proxy.Object);
            Mocker.GetMock<IQBittorrentProxySelector>().Setup(s => s.GetApiVersion(It.IsAny<QBittorrentSettings>(), It.IsAny<bool>())).Returns(new Version(2, 8, 3));

            var client = Mocker.Resolve<QBittorrent>();
            client.Definition = new DownloadClientDefinition { Id = 1, Name = "qBittorrent", Settings = new QBittorrentSettings { Host = "127.0.0.1", Port = 2222, MusicCategory = "music" } };
            Mocker.GetMock<IProvideDownloadClient>().Setup(p => p.Get(1)).Returns(client);
            Mocker.GetMock<IProvideDownloadClient>()
                  .Setup(p => p.GetDownloadClient(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<HashSet<int>>()))
                  .Returns(client);

            var grabbed = RemoteAlbum(7, null);
            grabbed.Release.DownloadUrl = "magnet:?xt=urn:btih:ZPBPA2P6ROZPKRHK44D5OW6NHXU5Z6KR&tr=udp://abc";

            await FluentActions.Awaiting(() => Subject.DownloadReport(grabbed, null)).Should().ThrowAsync<ReleaseAlreadyInDownloadClientException>();

            Decide(RemoteAlbum(7, null)).Accepted.Should().BeFalse();
            Decide(RemoteAlbum(9, Hash.ToLowerInvariant(), "another-listing")).Accepted.Should().BeFalse();
            proxy.Verify(s => s.IsTorrentLoaded(Hash.ToLowerInvariant(), It.IsAny<QBittorrentSettings>()), Times.Exactly(3));

            loaded = false;
            Decide(RemoteAlbum(7, null)).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_accept_releases_nothing_refused()
        {
            Decide(RemoteAlbum(7, Hash)).Accepted.Should().BeTrue();
        }
    }
}
