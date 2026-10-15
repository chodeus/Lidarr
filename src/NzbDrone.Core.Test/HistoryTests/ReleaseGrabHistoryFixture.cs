using System;
using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.History;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.HistoryTests
{
    [TestFixture]
    public class ReleaseGrabHistoryFixture : CoreTest
    {
        private const string Title = "Artist Name - Album Title (2026) [FLAC]";
        private const string Indexer = "Indexer One";

        private static readonly DateTime PublishDate = new DateTime(2026, 2, 6, 15, 39, 0, DateTimeKind.Utc);
        private static readonly DateTime GrabDate = new DateTime(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc);

        private static ReleaseInfo Usenet(string guid = "guid-1", string indexer = Indexer, DateTime? publishDate = null)
        {
            return new ReleaseInfo { Guid = guid, Title = Title, Indexer = indexer, PublishDate = publishDate ?? PublishDate };
        }

        private static TorrentInfo Torrent(string guid, string infoHash)
        {
            return new TorrentInfo { Guid = guid, Title = Title, Indexer = Indexer, PublishDate = PublishDate, InfoHash = infoHash };
        }

        private static EntityHistory Grab(string guid = "guid-1", string downloadId = "download-1", DateTime? date = null, string infoHash = null)
        {
            var history = new EntityHistory
            {
                EventType = EntityHistoryEventType.Grabbed,
                Date = date ?? GrabDate,
                SourceTitle = Title,
                DownloadId = downloadId
            };

            history.Data.Add("guid", guid);
            history.Data.Add("indexer", Indexer);
            history.Data.Add("publishedDate", "2026-02-06T15:39:00Z");

            if (infoHash != null)
            {
                history.Data.Add("torrentInfoHash", infoHash);
            }

            return history;
        }

        private static EntityHistory Failure(string downloadId, DateTime date)
        {
            return new EntityHistory { EventType = EntityHistoryEventType.DownloadFailed, Date = date, SourceTitle = Title, DownloadId = downloadId };
        }

        [Test]
        public void should_find_the_grab_of_the_same_guid()
        {
            ReleaseGrabHistory.Find(Usenet(indexer: "Indexer Two"), new List<EntityHistory> { Grab() }).Grabbed.Should().Be(GrabDate);
        }

        [Test]
        public void should_find_nothing_without_a_matching_grab()
        {
            ReleaseGrabHistory.Find(Usenet("guid-2", "Indexer Two"), new List<EntityHistory> { Grab() }).Should().BeNull();
        }

        [Test]
        public void should_use_the_newest_grab()
        {
            var history = new List<EntityHistory> { Grab(date: GrabDate.AddDays(1)), Grab() };

            ReleaseGrabHistory.Find(Usenet(), history).Grabbed.Should().Be(GrabDate.AddDays(1));
        }

        [TestCase("ABCDEF0123")]
        [TestCase("abcdef0123")]
        public void should_find_a_torrent_by_info_hash_when_the_guid_differs(string grabbedHash)
        {
            ReleaseGrabHistory.Find(Torrent("guid-2", "abcdef0123"), new List<EntityHistory> { Grab(infoHash: grabbedHash) }).Should().NotBeNull();
        }

        [Test]
        public void should_not_find_a_torrent_with_another_info_hash()
        {
            ReleaseGrabHistory.Find(Torrent("guid-2", "abcdef0123"), new List<EntityHistory> { Grab(infoHash: "9876543210") }).Should().BeNull();
        }

        [TestCase("Indexer One")]
        [TestCase("INDEXER ONE")]
        public void should_find_the_same_usenet_post_when_the_guid_differs(string indexer)
        {
            ReleaseGrabHistory.Find(Usenet("guid-2", indexer), new List<EntityHistory> { Grab() }).Should().NotBeNull();
        }

        [Test]
        public void should_not_find_a_usenet_post_from_another_indexer()
        {
            ReleaseGrabHistory.Find(Usenet("guid-2", "Indexer Two"), new List<EntityHistory> { Grab() }).Should().BeNull();
        }

        [Test]
        public void should_not_find_a_usenet_post_with_another_published_date()
        {
            ReleaseGrabHistory.Find(Usenet("guid-2", publishDate: PublishDate.AddMinutes(1)), new List<EntityHistory> { Grab() }).Should().BeNull();
        }

        [Test]
        public void should_report_when_the_grabbed_download_failed()
        {
            var failed = GrabDate.AddHours(2);
            var history = new List<EntityHistory> { Failure("download-1", failed), Grab() };

            ReleaseGrabHistory.Find(Usenet(), history).Failed.Should().Be(failed);
        }

        [Test]
        public void should_ignore_another_downloads_failure()
        {
            var history = new List<EntityHistory> { Failure("download-2", GrabDate.AddHours(2)), Grab() };

            ReleaseGrabHistory.Find(Usenet(), history).Failed.Should().BeNull();
        }

        [Test]
        public void should_ignore_a_failure_of_an_earlier_grab_with_the_same_download_id()
        {
            var history = new List<EntityHistory> { Grab(), Failure("download-1", GrabDate.AddHours(-2)), Grab(date: GrabDate.AddHours(-3)) };

            ReleaseGrabHistory.Find(Usenet(), history).Failed.Should().BeNull();
        }

        [Test]
        public void should_not_tie_a_failure_to_a_grab_without_a_download_id()
        {
            var history = new List<EntityHistory> { Failure(null, GrabDate.AddHours(2)), Grab(downloadId: null) };

            ReleaseGrabHistory.Find(Usenet(), history).Failed.Should().BeNull();
        }
    }
}
