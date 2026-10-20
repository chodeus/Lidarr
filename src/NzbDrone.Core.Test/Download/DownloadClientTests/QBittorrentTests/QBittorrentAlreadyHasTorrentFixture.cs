using System;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Clients;
using NzbDrone.Core.Download.Clients.QBittorrent;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MediaFiles.TorrentInfo;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Test.Download.DownloadClientTests.QBittorrentTests
{
    // A file of its own, so it doesn't collide with other branches' tests in QBittorrentFixture
    [TestFixture]
    public class QBittorrentAlreadyHasTorrentFixture : DownloadClientFixtureBase<QBittorrent>
    {
        [SetUp]
        public void Setup()
        {
            Subject.Definition = new DownloadClientDefinition();
            Subject.Definition.Settings = new QBittorrentSettings
            {
                Host = "127.0.0.1",
                Port = 2222,
                Username = "admin",
                Password = "pass",
                MusicCategory = "music"
            };

            Mocker.GetMock<ITorrentFileInfoReader>()
                  .Setup(s => s.GetHashFromTorrentFile(It.IsAny<byte[]>()))
                  .Returns("CBC2F069FE8BB2F544EAE707D75BCD3DE9DCF951");

            Mocker.GetMock<IHttpClient>()
                  .Setup(s => s.Get(It.IsAny<HttpRequest>()))
                  .Returns<HttpRequest>(r => new HttpResponse(r, new HttpHeader(), Array.Empty<byte>()));

            Mocker.GetMock<IQBittorrentProxy>()
                  .Setup(s => s.GetConfig(It.IsAny<QBittorrentSettings>()))
                  .Returns(new QBittorrentPreferences { DhtEnabled = true });

            Mocker.GetMock<IQBittorrentProxySelector>()
                  .Setup(s => s.GetProxy(It.IsAny<QBittorrentSettings>(), It.IsAny<bool>()))
                  .Returns(Mocker.GetMock<IQBittorrentProxy>().Object);
        }

        private static DownloadClientException ProxyHttpError(System.Net.HttpStatusCode statusCode)
        {
            var response = new HttpResponse(new HttpRequest("http://me.local/"), new HttpHeader(), Array.Empty<byte>(), statusCode);

            return new DownloadClientException("Failed to connect to qBittorrent, check your settings.", new HttpException(response));
        }

        private void GivenApiVersion(Version version)
        {
            Mocker.GetMock<IQBittorrentProxySelector>()
                  .Setup(s => s.GetApiVersion(It.IsAny<QBittorrentSettings>(), It.IsAny<bool>()))
                  .Returns(version);
        }

        private void GivenAddingThrows(Exception refusal, bool torrentLoaded)
        {
            GivenApiVersion(new Version(2, 0));

            Mocker.GetMock<IQBittorrentProxy>()
                  .Setup(s => s.AddTorrentFromFile(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<TorrentSeedConfiguration>(), It.IsAny<QBittorrentSettings>()))
                  .Throws(refusal);

            Mocker.GetMock<IQBittorrentProxy>()
                  .Setup(s => s.AddTorrentFromUrl(It.IsAny<string>(), It.IsAny<TorrentSeedConfiguration>(), It.IsAny<QBittorrentSettings>()))
                  .Throws(refusal);

            Mocker.GetMock<IQBittorrentProxy>()
                  .Setup(s => s.IsTorrentLoaded(It.IsAny<string>(), It.IsAny<QBittorrentSettings>()))
                  .Returns(torrentLoaded);
        }

        private void GivenConflictWhenAdding(bool torrentLoaded)
        {
            GivenAddingThrows(ProxyHttpError(System.Net.HttpStatusCode.Conflict), torrentLoaded);
        }

        private void GivenFailsWhenAdding(bool torrentLoaded)
        {
            GivenAddingThrows(new QBittorrentAddFailedException("Download client failed to add torrent"), torrentLoaded);
        }

        private void GivenTheCheckFails()
        {
            Mocker.GetMock<IQBittorrentProxy>()
                  .Setup(s => s.IsTorrentLoaded(It.IsAny<string>(), It.IsAny<QBittorrentSettings>()))
                  .Throws(new DownloadClientException("qBittorrent is restarting"));
        }

        private RemoteAlbum TorrentFileRelease()
        {
            var remoteAlbum = CreateRemoteAlbum();
            remoteAlbum.Release = new TorrentInfo { Title = _title, DownloadUrl = _downloadUrl, DownloadProtocol = Subject.Protocol };

            return remoteAlbum;
        }

        private RemoteAlbum MagnetRelease()
        {
            var remoteAlbum = CreateRemoteAlbum();
            remoteAlbum.Release.DownloadUrl = "magnet:?xt=urn:btih:ZPBPA2P6ROZPKRHK44D5OW6NHXU5Z6KR&tr=udp://abc";

            return remoteAlbum;
        }

        [Test]
        public void Download_should_report_a_torrent_file_the_client_already_has()
        {
            GivenConflictWhenAdding(true);

            Assert.ThrowsAsync<ReleaseAlreadyInDownloadClientException>(async () => await Subject.Download(TorrentFileRelease(), CreateIndexer()))
                  .InfoHash.Should().Be("CBC2F069FE8BB2F544EAE707D75BCD3DE9DCF951");

            Mocker.GetMock<IQBittorrentProxy>()
                  .Verify(s => s.IsTorrentLoaded("cbc2f069fe8bb2f544eae707d75bcd3de9dcf951", It.IsAny<QBittorrentSettings>()), Times.Once());
        }

        [Test]
        public void Download_should_report_a_magnet_link_the_client_already_has()
        {
            GivenConflictWhenAdding(true);

            Assert.ThrowsAsync<ReleaseAlreadyInDownloadClientException>(async () => await Subject.Download(MagnetRelease(), CreateIndexer()))
                  .InfoHash.Should().Be("CBC2F069FE8BB2F544EAE707D75BCD3DE9DCF951");
        }

        [Test]
        public void Download_should_report_any_other_conflict_as_a_plain_rejection()
        {
            GivenConflictWhenAdding(false);

            var thrown = Assert.ThrowsAsync<DownloadClientRejectedReleaseException>(async () => await Subject.Download(TorrentFileRelease(), CreateIndexer()));

            thrown.Should().NotBeOfType<ReleaseAlreadyInDownloadClientException>();
        }

        [Test]
        public void Download_should_report_a_plain_rejection_when_the_client_cannot_be_asked()
        {
            GivenConflictWhenAdding(true);
            GivenTheCheckFails();

            var thrown = Assert.ThrowsAsync<DownloadClientRejectedReleaseException>(async () => await Subject.Download(TorrentFileRelease(), CreateIndexer()));

            thrown.Should().NotBeOfType<ReleaseAlreadyInDownloadClientException>();
        }

        [Test]
        public void Download_should_report_a_conflict_as_a_plain_rejection_on_api_v1()
        {
            GivenConflictWhenAdding(true);
            GivenApiVersion(new Version(1, 2));

            var thrown = Assert.ThrowsAsync<DownloadClientRejectedReleaseException>(async () => await Subject.Download(TorrentFileRelease(), CreateIndexer()));

            thrown.Should().NotBeOfType<ReleaseAlreadyInDownloadClientException>();
            Mocker.GetMock<IQBittorrentProxy>()
                  .Verify(s => s.IsTorrentLoaded(It.IsAny<string>(), It.IsAny<QBittorrentSettings>()), Times.Never());
        }

        [Test]
        public void HasTorrent_should_answer_no_on_api_v1()
        {
            GivenApiVersion(new Version(1, 2));

            Mocker.GetMock<IQBittorrentProxy>()
                  .Setup(s => s.IsTorrentLoaded(It.IsAny<string>(), It.IsAny<QBittorrentSettings>()))
                  .Returns(true);

            Subject.HasTorrent("CBC2F069FE8BB2F544EAE707D75BCD3DE9DCF951").Should().BeFalse();
        }

        [Test]
        public void Download_should_report_a_torrent_file_the_client_already_has_when_it_replies_fails()
        {
            GivenFailsWhenAdding(true);

            Assert.ThrowsAsync<ReleaseAlreadyInDownloadClientException>(async () => await Subject.Download(TorrentFileRelease(), CreateIndexer()))
                  .InfoHash.Should().Be("CBC2F069FE8BB2F544EAE707D75BCD3DE9DCF951");

            Mocker.GetMock<IQBittorrentProxy>()
                  .Verify(s => s.IsTorrentLoaded("cbc2f069fe8bb2f544eae707d75bcd3de9dcf951", It.IsAny<QBittorrentSettings>()), Times.Once());
        }

        [Test]
        public void Download_should_report_a_magnet_link_the_client_already_has_when_it_replies_fails()
        {
            GivenFailsWhenAdding(true);

            Assert.ThrowsAsync<ReleaseAlreadyInDownloadClientException>(async () => await Subject.Download(MagnetRelease(), CreateIndexer()))
                  .InfoHash.Should().Be("CBC2F069FE8BB2F544EAE707D75BCD3DE9DCF951");

            Mocker.GetMock<IQBittorrentProxy>()
                  .Verify(s => s.IsTorrentLoaded("cbc2f069fe8bb2f544eae707d75bcd3de9dcf951", It.IsAny<QBittorrentSettings>()), Times.Once());
        }

        [Test]
        public void Download_should_keep_a_fails_reply_for_a_torrent_the_client_lacks_as_a_failed_add()
        {
            GivenFailsWhenAdding(false);

            Assert.ThrowsAsync<QBittorrentAddFailedException>(async () => await Subject.Download(TorrentFileRelease(), CreateIndexer()));
        }

        [Test]
        public void Download_should_keep_a_fails_reply_as_a_failed_add_when_the_client_cannot_be_asked()
        {
            GivenFailsWhenAdding(true);
            GivenTheCheckFails();

            Assert.ThrowsAsync<QBittorrentAddFailedException>(async () => await Subject.Download(TorrentFileRelease(), CreateIndexer()));
        }
    }
}
