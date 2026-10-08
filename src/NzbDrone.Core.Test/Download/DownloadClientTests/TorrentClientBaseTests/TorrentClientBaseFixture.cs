using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Blocklisting;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.MediaFiles.TorrentInfo;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Validation;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Download.DownloadClientTests.TorrentClientBaseTests
{
    [TestFixture]
    public class TorrentClientBaseFixture : DownloadClientFixtureBase<TestTorrentClient>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<ITorrentFileInfoReader>()
                  .Setup(s => s.GetHashFromTorrentFile(It.IsAny<byte[]>()))
                  .Returns("HASH");
        }

        private IIndexer CreateIndexerWithFailDownloads(bool rejectWhileGrabbing, params FailDownloads[] failDownloads)
        {
            var indexer = CreateIndexer();

            indexer.Definition = new IndexerDefinition
            {
                Settings = new TestTorrentIndexerSettings
                {
                    RejectTorrentFilesWithBlockedExtensionsWhileGrabbing = rejectWhileGrabbing,
                    FailDownloads = failDownloads.Cast<int>().ToList()
                }
            };

            return indexer;
        }

        private void GivenTorrentFiles(params string[] fileNames)
        {
            Mocker.GetMock<ITorrentFileInfoReader>()
                  .Setup(s => s.GetFileNamesFromTorrentFile(It.IsAny<byte[]>()))
                  .Returns(new List<string>(fileNames));
        }

        private void VerifyBlocked(RemoteAlbum remoteAlbum, int times)
        {
            Mocker.GetMock<IBlocklistService>()
                  .Verify(s => s.Block(remoteAlbum, It.IsAny<string>()), Times.Exactly(times));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void should_blocklist_and_throw_when_torrent_contains_an_executable(bool preferTorrentFile)
        {
            Subject.SetPreferTorrentFile(preferTorrentFile);

            var remoteAlbum = CreateRemoteAlbum();
            GivenTorrentFiles("Artist - Album/01 - Track.flac", "Artist - Album/Setup.exe");

            Assert.ThrowsAsync<ReleaseBlockedException>(async () => await Subject.Download(remoteAlbum, CreateIndexerWithFailDownloads(true, FailDownloads.Executables)));

            VerifyBlocked(remoteAlbum, 1);
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_blocklist_user_defined_extension_case_insensitively()
        {
            Mocker.GetMock<IConfigService>()
                  .SetupGet(s => s.UserRejectedExtensions)
                  .Returns("nfo");

            var remoteAlbum = CreateRemoteAlbum();
            GivenTorrentFiles("Artist - Album/01 - Track.flac", "Artist - Album/Album.NFO");

            Assert.ThrowsAsync<ReleaseBlockedException>(async () => await Subject.Download(remoteAlbum, CreateIndexerWithFailDownloads(true, FailDownloads.UserDefinedExtensions)));

            VerifyBlocked(remoteAlbum, 1);
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_name_every_rejected_group_in_the_blocklist_message()
        {
            Mocker.GetMock<IConfigService>()
                  .SetupGet(s => s.UserRejectedExtensions)
                  .Returns("nfo");

            var remoteAlbum = CreateRemoteAlbum();
            GivenTorrentFiles("01 - Track.flac", "setup.exe", "payload.lnk", "info.nfo");

            Assert.ThrowsAsync<ReleaseBlockedException>(async () => await Subject.Download(remoteAlbum, CreateIndexerWithFailDownloads(true, FailDownloads.Executables, FailDownloads.PotentiallyDangerous, FailDownloads.UserDefinedExtensions)));

            Mocker.GetMock<IBlocklistService>()
                  .Verify(s => s.Block(remoteAlbum, It.Is<string>(m => m.Contains("potentially dangerous") && m.Contains("executables") && m.Contains("user defined"))), Times.Once());
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public async Task should_not_blocklist_a_torrent_holding_only_safe_files()
        {
            var remoteAlbum = CreateRemoteAlbum();
            GivenTorrentFiles("Artist - Album/01 - Track.flac", "Artist - Album/cover.jpg");

            await Subject.Download(remoteAlbum, CreateIndexerWithFailDownloads(true, FailDownloads.Executables, FailDownloads.PotentiallyDangerous));

            VerifyBlocked(remoteAlbum, 0);
        }

        [Test]
        public async Task should_not_read_the_file_list_when_the_option_is_off()
        {
            await Subject.Download(CreateRemoteAlbum(), CreateIndexerWithFailDownloads(false, FailDownloads.Executables));

            Mocker.GetMock<ITorrentFileInfoReader>()
                  .Verify(s => s.GetFileNamesFromTorrentFile(It.IsAny<byte[]>()), Times.Never());
        }

        [Test]
        public async Task should_not_read_the_file_list_when_no_file_type_fails()
        {
            await Subject.Download(CreateRemoteAlbum(), CreateIndexerWithFailDownloads(true));

            Mocker.GetMock<ITorrentFileInfoReader>()
                  .Verify(s => s.GetFileNamesFromTorrentFile(It.IsAny<byte[]>()), Times.Never());
        }

        [Test]
        public async Task should_not_read_the_file_list_for_a_magnet_link()
        {
            var remoteAlbum = CreateRemoteAlbum();
            remoteAlbum.Release.DownloadUrl = "magnet:?xt=urn:btih:c12fe1c06bba254a9dc9f519b335aa7c1367a88a";

            await Subject.Download(remoteAlbum, CreateIndexerWithFailDownloads(true, FailDownloads.Executables));

            Mocker.GetMock<ITorrentFileInfoReader>()
                  .Verify(s => s.GetFileNamesFromTorrentFile(It.IsAny<byte[]>()), Times.Never());
        }
    }

    public class TestTorrentIndexerSettings : ITorrentIndexerSettings
    {
        public string BaseUrl { get; set; }
        public int? EarlyReleaseLimit { get; set; }
        public IEnumerable<int> FailDownloads { get; set; }
        public int MinimumSeeders { get; set; }
        public SeedCriteriaSettings SeedCriteria { get; set; }
        public bool RejectBlocklistedTorrentHashesWhileGrabbing { get; set; }
        public bool RejectTorrentFilesWithBlockedExtensionsWhileGrabbing { get; set; }

        public NzbDroneValidationResult Validate() => new NzbDroneValidationResult();
    }
}
