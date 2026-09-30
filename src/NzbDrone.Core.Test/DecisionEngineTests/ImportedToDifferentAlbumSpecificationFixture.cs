using System;
using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.History;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.DecisionEngineTests
{
    [TestFixture]
    public class ImportedToDifferentAlbumSpecificationFixture : CoreTest<ImportedToDifferentAlbumSpecification>
    {
        private const int ALBUM_ID = 1;
        private const int OTHER_ALBUM_ID = 2;
        private const string TITLE = "Some.Artist-Some.Album-2018-320kbps-CD-Lidarr";

        private RemoteAlbum _remoteAlbum;
        private List<EntityHistory> _albumGrabs;
        private List<EntityHistory> _downloadHistory;
        private string _downloadId;

        [SetUp]
        public void Setup()
        {
            _remoteAlbum = new RemoteAlbum
            {
                Artist = Builder<Artist>.CreateNew().Build(),
                Albums = new List<Album> { new Album { Id = ALBUM_ID, Title = "Some Album" } },
                Release = Builder<ReleaseInfo>.CreateNew()
                                              .With(r => r.Title = TITLE)
                                              .Build()
            };

            _downloadId = Guid.NewGuid().ToString().ToUpper();
            _albumGrabs = new List<EntityHistory>();
            _downloadHistory = new List<EntityHistory>();

            Mocker.GetMock<IConfigService>()
                  .SetupGet(s => s.EnableCompletedDownloadHandling)
                  .Returns(true);

            Mocker.GetMock<IHistoryService>()
                  .Setup(s => s.GetByAlbum(It.IsAny<int>(), EntityHistoryEventType.Grabbed))
                  .Returns(_albumGrabs);

            Mocker.GetMock<IHistoryService>()
                  .Setup(s => s.FindByDownloadId(_downloadId))
                  .Returns(_downloadHistory);
        }

        private void GivenGrab(string sourceTitle, int daysAgo = 1, params int[] albumIds)
        {
            var date = DateTime.UtcNow.AddDays(-daysAgo);

            _albumGrabs.Add(new EntityHistory
            {
                DownloadId = _downloadId,
                SourceTitle = sourceTitle,
                AlbumId = ALBUM_ID,
                Date = date,
                EventType = EntityHistoryEventType.Grabbed
            });

            foreach (var albumId in albumIds.Length == 0 ? new[] { ALBUM_ID } : albumIds)
            {
                GivenDownloadEvent(albumId, EntityHistoryEventType.Grabbed, date);
            }
        }

        private void GivenDownloadEvent(int albumId, EntityHistoryEventType eventType, DateTime? date = null)
        {
            _downloadHistory.Add(new EntityHistory
            {
                DownloadId = _downloadId,
                SourceTitle = TITLE,
                AlbumId = albumId,
                Date = date ?? DateTime.UtcNow,
                EventType = eventType
            });
        }

        private void GivenTracksImportedInto(params int[] albumIds)
        {
            foreach (var albumId in albumIds)
            {
                GivenDownloadEvent(albumId, EntityHistoryEventType.TrackFileImported);
            }
        }

        private void GivenEarlierAttempt(int daysAgo, params int[] importedAlbumIds)
        {
            var date = DateTime.UtcNow.AddDays(-daysAgo);

            _albumGrabs.Add(new EntityHistory
            {
                DownloadId = _downloadId,
                SourceTitle = TITLE,
                AlbumId = ALBUM_ID,
                Date = date,
                EventType = EntityHistoryEventType.Grabbed
            });

            GivenDownloadEvent(ALBUM_ID, EntityHistoryEventType.Grabbed, date);

            foreach (var albumId in importedAlbumIds)
            {
                GivenDownloadEvent(albumId, EntityHistoryEventType.TrackFileImported, date.AddHours(1));
            }
        }

        private void GivenTorrentRelease(string infoHash, string title)
        {
            _remoteAlbum.Release = Builder<TorrentInfo>.CreateNew()
                                                       .With(t => t.DownloadProtocol = nameof(TorrentDownloadProtocol))
                                                       .With(t => t.InfoHash = infoHash)
                                                       .With(t => t.Title = title)
                                                       .Build();
        }

        [Test]
        public void should_be_accepted_if_CDH_is_disabled()
        {
            GivenGrab(TITLE);
            GivenTracksImportedInto(OTHER_ALBUM_ID);

            Mocker.GetMock<IConfigService>()
                  .SetupGet(s => s.EnableCompletedDownloadHandling)
                  .Returns(false);

            Subject.IsSatisfiedBy(_remoteAlbum, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_be_accepted_if_release_was_not_grabbed_for_album()
        {
            Subject.IsSatisfiedBy(_remoteAlbum, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_be_accepted_if_last_grab_has_not_been_imported()
        {
            GivenGrab(TITLE);

            Subject.IsSatisfiedBy(_remoteAlbum, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_be_rejected_if_last_grab_was_imported_into_a_different_album()
        {
            GivenGrab(TITLE);
            GivenTracksImportedInto(OTHER_ALBUM_ID, OTHER_ALBUM_ID);

            Subject.IsSatisfiedBy(_remoteAlbum, null).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_be_rejected_if_release_title_differs_only_by_case()
        {
            GivenGrab(TITLE.ToLowerInvariant());
            GivenTracksImportedInto(OTHER_ALBUM_ID);

            Subject.IsSatisfiedBy(_remoteAlbum, null).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_be_accepted_if_last_grab_was_imported_into_the_album()
        {
            GivenGrab(TITLE);
            GivenTracksImportedInto(ALBUM_ID, ALBUM_ID);

            Subject.IsSatisfiedBy(_remoteAlbum, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_be_accepted_if_some_tracks_were_imported_into_the_album()
        {
            GivenGrab(TITLE);
            GivenTracksImportedInto(ALBUM_ID, OTHER_ALBUM_ID);

            Subject.IsSatisfiedBy(_remoteAlbum, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_be_rejected_if_only_the_download_event_names_the_album()
        {
            GivenGrab(TITLE);
            GivenTracksImportedInto(OTHER_ALBUM_ID);
            GivenDownloadEvent(ALBUM_ID, EntityHistoryEventType.DownloadImported);

            Subject.IsSatisfiedBy(_remoteAlbum, null).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_be_accepted_if_grab_has_no_download_id()
        {
            GivenGrab(TITLE);
            GivenTracksImportedInto(OTHER_ALBUM_ID);
            _albumGrabs.ForEach(h => h.DownloadId = null);

            Subject.IsSatisfiedBy(_remoteAlbum, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_only_consider_the_last_grab_of_the_release()
        {
            var olderDownloadId = Guid.NewGuid().ToString().ToUpper();

            GivenGrab(TITLE);
            GivenTracksImportedInto(ALBUM_ID);

            _albumGrabs.Add(new EntityHistory
            {
                DownloadId = olderDownloadId,
                SourceTitle = TITLE,
                AlbumId = ALBUM_ID,
                Date = DateTime.UtcNow.AddDays(-3),
                EventType = EntityHistoryEventType.Grabbed
            });

            Mocker.GetMock<IHistoryService>()
                  .Setup(s => s.FindByDownloadId(olderDownloadId))
                  .Returns(new List<EntityHistory>
                  {
                      new EntityHistory { DownloadId = olderDownloadId, AlbumId = ALBUM_ID, EventType = EntityHistoryEventType.Grabbed },
                      new EntityHistory { DownloadId = olderDownloadId, AlbumId = OTHER_ALBUM_ID, EventType = EntityHistoryEventType.TrackFileImported }
                  });

            Subject.IsSatisfiedBy(_remoteAlbum, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_be_rejected_if_the_last_grab_went_elsewhere_after_an_earlier_one_imported_into_the_album()
        {
            GivenGrab(TITLE);
            GivenEarlierAttempt(3, ALBUM_ID);
            GivenTracksImportedInto(OTHER_ALBUM_ID);

            Subject.IsSatisfiedBy(_remoteAlbum, null).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_be_accepted_if_the_last_grab_imported_nothing_after_an_earlier_one_went_elsewhere()
        {
            GivenGrab(TITLE);
            GivenEarlierAttempt(3, OTHER_ALBUM_ID);

            Subject.IsSatisfiedBy(_remoteAlbum, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_be_accepted_if_multi_album_grab_was_imported_into_another_of_its_albums()
        {
            GivenGrab(TITLE, 1, ALBUM_ID, OTHER_ALBUM_ID);
            GivenTracksImportedInto(OTHER_ALBUM_ID);

            Subject.IsSatisfiedBy(_remoteAlbum, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_be_accepted_if_last_grab_is_older_than_14_days()
        {
            GivenGrab(TITLE, 15);
            GivenTracksImportedInto(OTHER_ALBUM_ID);

            Subject.IsSatisfiedBy(_remoteAlbum, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_be_rejected_if_last_grab_is_within_14_days()
        {
            GivenGrab(TITLE, 13);
            GivenTracksImportedInto(OTHER_ALBUM_ID);

            Subject.IsSatisfiedBy(_remoteAlbum, null).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_be_accepted_if_a_different_release_was_grabbed()
        {
            GivenGrab("Some.Artist-Some.Other.Album-2018-FLAC-Lidarr");
            GivenTracksImportedInto(OTHER_ALBUM_ID);

            Subject.IsSatisfiedBy(_remoteAlbum, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_be_rejected_if_torrent_hash_matches_grabbed_download_id()
        {
            GivenTorrentRelease(_downloadId.ToLowerInvariant(), "Some.Artist-Some.Album-2018-FLAC-Renamed");
            GivenGrab(TITLE);
            GivenTracksImportedInto(OTHER_ALBUM_ID);

            Subject.IsSatisfiedBy(_remoteAlbum, null).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_be_accepted_if_torrent_hash_is_null_and_title_differs()
        {
            GivenTorrentRelease(null, "Some.Artist-Some.Album-2018-FLAC-Renamed");
            GivenGrab(TITLE);
            GivenTracksImportedInto(OTHER_ALBUM_ID);

            Subject.IsSatisfiedBy(_remoteAlbum, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_be_rejected_if_any_album_in_release_had_it_imported_elsewhere()
        {
            _remoteAlbum.Albums.Insert(0, new Album { Id = 3, Title = "Some Other Album" });

            Mocker.GetMock<IHistoryService>()
                  .Setup(s => s.GetByAlbum(3, EntityHistoryEventType.Grabbed))
                  .Returns(new List<EntityHistory>());

            GivenGrab(TITLE);
            GivenTracksImportedInto(OTHER_ALBUM_ID);

            Subject.IsSatisfiedBy(_remoteAlbum, null).Accepted.Should().BeFalse();
        }
    }
}
