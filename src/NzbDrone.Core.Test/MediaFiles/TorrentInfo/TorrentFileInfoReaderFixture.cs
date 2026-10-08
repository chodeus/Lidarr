using System.Text;
using FluentAssertions;
using NLog;
using NLog.Targets;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles.TorrentInfo;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.TorrentInfo
{
    [TestFixture]
    public class TorrentFileInfoReaderFixture : CoreTest<TorrentFileInfoReader>
    {
        private static byte[] MakeSingleFileTorrent(string name)
        {
            return Encoding.UTF8.GetBytes($"d4:infod6:lengthi100e4:name{name.Length}:{name}12:piece lengthi16384e6:pieces20:{new string('a', 20)}ee");
        }

        private static byte[] MakeMultiFileTorrent(params string[] fileNames)
        {
            var builder = new StringBuilder("d4:infod5:filesl");

            foreach (var fileName in fileNames)
            {
                builder.Append($"d6:lengthi100e4:pathl{fileName.Length}:{fileName}ee");
            }

            builder.Append($"e4:name5:Album12:piece lengthi16384e6:pieces20:{new string('a', 20)}ee");

            return Encoding.UTF8.GetBytes(builder.ToString());
        }

        [Test]
        public void should_get_file_name_from_single_file_torrent()
        {
            Subject.GetFileNamesFromTorrentFile(MakeSingleFileTorrent("01 - Track.flac"))
                   .Should().ContainSingle(f => f.EndsWith("01 - Track.flac"));
        }

        [Test]
        public void should_get_file_names_from_multi_file_torrent()
        {
            var fileNames = Subject.GetFileNamesFromTorrentFile(MakeMultiFileTorrent("01 - Track.flac", "Setup.exe"));

            fileNames.Should().HaveCount(2);
            fileNames.Should().Contain(f => f.EndsWith("Setup.exe"));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void should_not_log_the_contents_of_an_invalid_torrent(bool readFileNames)
        {
            var memory = new MemoryTarget("torrentReaderLog") { Layout = "${message}" };
            LogManager.Configuration.AddRuleForAllLevels(memory);
            LogManager.ReconfigExistingLoggers();

            try
            {
                var contents = Encoding.UTF8.GetBytes("d8:announce46:http://tracker.invalid/announce?passkey=SECRETe");

                Assert.Catch(() =>
                {
                    if (readFileNames)
                    {
                        Subject.GetFileNamesFromTorrentFile(contents);
                    }
                    else
                    {
                        Subject.GetHashFromTorrentFile(contents);
                    }
                });

                memory.Logs.Should().NotBeEmpty();
                memory.Logs.Should().NotContain(l => l.Contains("SECRET"));
            }
            finally
            {
                LogManager.Configuration.RemoveTarget(memory.Name);
                LogManager.ReconfigExistingLoggers();
            }
        }

        [Test]
        public void should_throw_when_torrent_is_invalid()
        {
            Assert.Throws<MonoTorrent.TorrentException>(() => Subject.GetFileNamesFromTorrentFile(Encoding.UTF8.GetBytes("not a torrent")));
        }
    }
}
