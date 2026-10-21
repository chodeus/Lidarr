using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Aggregation;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Music;
using NzbDrone.Core.Music.Events;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download.TrackedDownloads
{
    [TestFixture]
    public class TrackedDownloadServiceFixture : CoreTest<TrackedDownloadService>
    {
        private void GivenDownloadHistory()
        {
            Mocker.GetMock<IHistoryService>()
                .Setup(s => s.FindByDownloadId(It.Is<string>(sr => sr == "35238")))
                .Returns(new List<EntityHistory>()
                {
                 new EntityHistory()
                {
                     DownloadId = "35238",
                     SourceTitle = "Audio Artist - Audio Album [2018 - FLAC]",
                     ArtistId = 5,
                     AlbumId = 4,
                }
                });
        }

        private void GivenGrabHistory(bool withIndexerFlags)
        {
            var data = new Dictionary<string, string> { { "indexer", "Test Indexer" } };

            if (withIndexerFlags)
            {
                data.Add("indexerFlags", "0");
            }

            Mocker.GetMock<IHistoryService>()
                .Setup(s => s.FindByDownloadId("35238"))
                .Returns(new List<EntityHistory>
                {
                    new EntityHistory
                    {
                        DownloadId = "35238",
                        EventType = EntityHistoryEventType.Grabbed,
                        SourceTitle = "Audio Artist - Audio Album [2018 - FLAC]",
                        ArtistId = 5,
                        AlbumId = 4,
                        Data = data
                    }
                });

            Mocker.GetMock<IDownloadHistoryService>()
                .Setup(s => s.GetLatestDownloadHistoryItem("35238"))
                .Returns(new DownloadHistory { EventType = DownloadHistoryEventType.DownloadImported, IndexerId = 0 });

            Mocker.GetMock<IDownloadHistoryService>()
                .Setup(s => s.GetLatestGrab("35238"))
                .Returns(new DownloadHistory { EventType = DownloadHistoryEventType.DownloadGrabbed, IndexerId = 7 });
        }

        private TrackedDownload GivenTrackedTorrent(string title = "The torrent release folder")
        {
            var client = new DownloadClientDefinition
            {
                Id = 1,
                Protocol = nameof(TorrentDownloadProtocol)
            };

            var item = new DownloadClientItem
            {
                Title = title,
                DownloadId = "35238",
                DownloadClientInfo = new DownloadClientItemClientInfo
                {
                    Protocol = client.Protocol,
                    Id = client.Id,
                    Name = client.Name
                }
            };

            return Subject.TrackDownload(client, item);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void should_set_release_indexer_from_grab_history(bool withIndexerFlags)
        {
            GivenGrabHistory(withIndexerFlags);

            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.Map(It.IsAny<ParsedAlbumInfo>(), It.IsAny<int>(), It.IsAny<IEnumerable<int>>()))
                  .Returns<ParsedAlbumInfo, int, IEnumerable<int>>((info, _, _) => new RemoteAlbum
                  {
                      Artist = new Artist { Id = 5 },
                      Albums = new List<Album> { new Album { Id = 4 } },
                      ParsedAlbumInfo = info
                  });

            var release = GivenTrackedTorrent().RemoteAlbum.Release;

            release.Should().NotBeNull();
            release.IndexerId.Should().Be(7);
            release.Indexer.Should().Be("Test Indexer");
            release.Title.Should().Be("Audio Artist - Audio Album [2018 - FLAC]");
        }

        [Test]
        public void should_title_the_release_after_the_grab_when_the_client_title_differs()
        {
            GivenGrabHistory(true);

            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.Map(It.IsAny<ParsedAlbumInfo>(), It.IsAny<SearchCriteriaBase>()))
                  .Returns<ParsedAlbumInfo, SearchCriteriaBase>((info, _) => new RemoteAlbum
                  {
                      Artist = new Artist { Id = 5 },
                      Albums = new List<Album> { new Album { Id = 4 } },
                      ParsedAlbumInfo = info
                  });

            var release = GivenTrackedTorrent("Audio Artist - Audio Album [2018 - MP3-320]").RemoteAlbum.Release;

            release.Title.Should().Be("Audio Artist - Audio Album [2018 - FLAC]");
        }

        [Test]
        public void should_keep_the_grabbed_release_when_a_tracked_download_is_remapped()
        {
            GivenGrabHistory(true);

            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.Map(It.IsAny<ParsedAlbumInfo>(), It.IsAny<SearchCriteriaBase>()))
                  .Returns<ParsedAlbumInfo, SearchCriteriaBase>((info, _) => new RemoteAlbum
                  {
                      Artist = new Artist { Id = 5 },
                      Albums = new List<Album> { new Album { Id = 4 } },
                      ParsedAlbumInfo = info
                  });

            GivenTrackedTorrent("Audio Artist - Audio Album [2018 - FLAC]");

            Subject.Handle(new AlbumDeletedEvent(new Album { Id = 4 }, false, false));

            var release = Subject.GetTrackedDownloads().Single().RemoteAlbum.Release;

            release.Should().NotBeNull();
            release.IndexerId.Should().Be(7);
        }

        [Test]
        public void should_track_downloads_using_the_source_title_if_it_cannot_be_found_using_the_download_title()
        {
            GivenDownloadHistory();

            var remoteAlbum = new RemoteAlbum
            {
                Artist = new Artist() { Id = 5 },
                Albums = new List<Album> { new Album { Id = 4 } },
                ParsedAlbumInfo = new ParsedAlbumInfo()
                {
                    AlbumTitle = "Audio Album",
                    ArtistName = "Audio Artist"
                }
            };

            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.Map(It.Is<ParsedAlbumInfo>(i => i.AlbumTitle == "Audio Album" && i.ArtistName == "Audio Artist"), It.IsAny<int>(), It.IsAny<IEnumerable<int>>()))
                  .Returns(remoteAlbum);

            var client = new DownloadClientDefinition()
            {
                Id = 1,
                Protocol = nameof(TorrentDownloadProtocol)
            };

            var item = new DownloadClientItem()
            {
                Title = "The torrent release folder",
                DownloadId = "35238",
                DownloadClientInfo = new DownloadClientItemClientInfo
                {
                    Protocol = client.Protocol,
                    Id = client.Id,
                    Name = client.Name
                }
            };

            var trackedDownload = Subject.TrackDownload(client, item);

            trackedDownload.Should().NotBeNull();
            trackedDownload.RemoteAlbum.Should().NotBeNull();
            trackedDownload.RemoteAlbum.Artist.Should().NotBeNull();
            trackedDownload.RemoteAlbum.Artist.Id.Should().Be(5);
            trackedDownload.RemoteAlbum.Albums.First().Id.Should().Be(4);
        }

        [Test]
        public void should_unmap_tracked_download_if_album_deleted()
        {
            GivenDownloadHistory();

            var remoteAlbum = new RemoteAlbum
            {
                Artist = new Artist() { Id = 5 },
                Albums = new List<Album> { new Album { Id = 4 } },
                ParsedAlbumInfo = new ParsedAlbumInfo()
                {
                    AlbumTitle = "Audio Album",
                    ArtistName = "Audio Artist"
                }
            };

            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.Map(It.Is<ParsedAlbumInfo>(i => i.AlbumTitle == "Audio Album" && i.ArtistName == "Audio Artist"), It.IsAny<int>(), It.IsAny<IEnumerable<int>>()))
                  .Returns(remoteAlbum);

            var client = new DownloadClientDefinition()
            {
                Id = 1,
                Protocol = nameof(TorrentDownloadProtocol)
            };

            var item = new DownloadClientItem()
            {
                Title = "Audio Artist - Audio Album [2018 - FLAC]",
                DownloadId = "35238",
                DownloadClientInfo = new DownloadClientItemClientInfo
                {
                    Protocol = client.Protocol,
                    Id = client.Id,
                    Name = client.Name
                }
            };

            // get a tracked download in place
            var trackedDownload = Subject.TrackDownload(client, item);
            Subject.GetTrackedDownloads().Should().HaveCount(1);

            // simulate deletion - album no longer maps
            Mocker.GetMock<IParsingService>()
                .Setup(s => s.Map(It.Is<ParsedAlbumInfo>(i => i.AlbumTitle == "Audio Album" && i.ArtistName == "Audio Artist"), It.IsAny<int>(), It.IsAny<IEnumerable<int>>()))
                .Returns(default(RemoteAlbum));

            // handle deletion event
            Subject.Handle(new AlbumDeletedEvent(remoteAlbum.Albums.First(), false, false));

            // verify download has null remote album
            var trackedDownloads = Subject.GetTrackedDownloads();
            trackedDownloads.Should().HaveCount(1);
            trackedDownloads.First().RemoteAlbum.Should().BeNull();
        }

        [Test]
        public void should_unmap_tracked_download_if_album_removed()
        {
            GivenDownloadHistory();

            var remoteAlbum = new RemoteAlbum
            {
                Artist = new Artist() { Id = 5 },
                Albums = new List<Album> { new Album { Id = 4 } },
                ParsedAlbumInfo = new ParsedAlbumInfo()
                {
                    AlbumTitle = "Audio Album",
                    ArtistName = "Audio Artist"
                }
            };

            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.Map(It.Is<ParsedAlbumInfo>(i => i.AlbumTitle == "Audio Album" && i.ArtistName == "Audio Artist"), It.IsAny<int>(), It.IsAny<IEnumerable<int>>()))
                  .Returns(remoteAlbum);

            var client = new DownloadClientDefinition()
            {
                Id = 1,
                Protocol = nameof(TorrentDownloadProtocol)
            };

            var item = new DownloadClientItem()
            {
                Title = "Audio Artist - Audio Album [2018 - FLAC]",
                DownloadId = "35238",
                DownloadClientInfo = new DownloadClientItemClientInfo
                {
                    Protocol = client.Protocol,
                    Id = client.Id,
                    Name = client.Name
                }
            };

            Subject.TrackDownload(client, item);
            Subject.GetTrackedDownloads().Should().HaveCount(1);

            // simulate deletion - album no longer maps
            Mocker.GetMock<IParsingService>()
                .Setup(s => s.Map(It.Is<ParsedAlbumInfo>(i => i.AlbumTitle == "Audio Album" && i.ArtistName == "Audio Artist"), It.IsAny<int>(), It.IsAny<IEnumerable<int>>()))
                .Returns(default(RemoteAlbum));

            Subject.Handle(new AlbumInfoRefreshedEvent(remoteAlbum.Artist, new List<Album>(), new List<Album>(), remoteAlbum.Albums));

            var trackedDownloads = Subject.GetTrackedDownloads();
            trackedDownloads.Should().HaveCount(1);
            trackedDownloads.First().RemoteAlbum.Should().BeNull();
        }

        [Test]
        public void should_not_throw_when_processing_deleted_albums()
        {
            GivenDownloadHistory();

            var remoteAlbum = new RemoteAlbum
            {
                Artist = new Artist() { Id = 5 },
                Albums = new List<Album> { new Album { Id = 4 } },
                ParsedAlbumInfo = new ParsedAlbumInfo()
                {
                    AlbumTitle = "Audio Album",
                    ArtistName = "Audio Artist"
                }
            };

            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.Map(It.Is<ParsedAlbumInfo>(i => i.AlbumTitle == "Audio Album" && i.ArtistName == "Audio Artist"), It.IsAny<int>(), It.IsAny<IEnumerable<int>>()))
                  .Returns(remoteAlbum);

            var client = new DownloadClientDefinition()
            {
                Id = 1,
                Protocol = nameof(TorrentDownloadProtocol)
            };

            var item = new DownloadClientItem()
            {
                Title = "Audio Artist - Audio Album [2018 - FLAC]",
                DownloadId = "35238",
                DownloadClientInfo = new DownloadClientItemClientInfo
                {
                    Protocol = client.Protocol,
                    Id = client.Id,
                    Name = client.Name
                }
            };

            Subject.TrackDownload(client, item);
            Subject.GetTrackedDownloads().Should().HaveCount(1);

            // simulate deletion - album no longer maps
            Mocker.GetMock<IParsingService>()
                .Setup(s => s.Map(It.Is<ParsedAlbumInfo>(i => i.AlbumTitle == "Audio Album" && i.ArtistName == "Audio Artist"), It.IsAny<int>(), It.IsAny<IEnumerable<int>>()))
                .Returns(default(RemoteAlbum));

            Subject.Handle(new AlbumInfoRefreshedEvent(remoteAlbum.Artist, new List<Album>(), new List<Album>(), remoteAlbum.Albums));

            var trackedDownloads = Subject.GetTrackedDownloads();
            trackedDownloads.Should().HaveCount(1);
            trackedDownloads.First().RemoteAlbum.Should().BeNull();
        }

        [Test]
        public void should_not_throw_when_processing_deleted_artist()
        {
            GivenDownloadHistory();

            var remoteAlbum = new RemoteAlbum
            {
                Artist = new Artist() { Id = 5 },
                Albums = new List<Album> { new Album { Id = 4 } },
                ParsedAlbumInfo = new ParsedAlbumInfo()
                {
                    AlbumTitle = "Audio Album",
                    ArtistName = "Audio Artist"
                }
            };

            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.Map(It.Is<ParsedAlbumInfo>(i => i.AlbumTitle == "Audio Album" && i.ArtistName == "Audio Artist"), It.IsAny<int>(), It.IsAny<IEnumerable<int>>()))
                  .Returns(remoteAlbum);

            var client = new DownloadClientDefinition()
            {
                Id = 1,
                Protocol = nameof(TorrentDownloadProtocol)
            };

            var item = new DownloadClientItem()
            {
                Title = "Audio Artist - Audio Album [2018 - FLAC]",
                DownloadId = "35238",
                DownloadClientInfo = new DownloadClientItemClientInfo
                {
                    Protocol = client.Protocol,
                    Id = client.Id,
                    Name = client.Name
                }
            };

            Subject.TrackDownload(client, item);
            Subject.GetTrackedDownloads().Should().HaveCount(1);

            // simulate deletion - album no longer maps
            Mocker.GetMock<IParsingService>()
                .Setup(s => s.Map(It.Is<ParsedAlbumInfo>(i => i.AlbumTitle == "Audio Album" && i.ArtistName == "Audio Artist"), It.IsAny<int>(), It.IsAny<IEnumerable<int>>()))
                .Returns(default(RemoteAlbum));

            Subject.Handle(new ArtistsDeletedEvent(new List<Artist> { remoteAlbum.Artist }, true, true));

            var trackedDownloads = Subject.GetTrackedDownloads();
            trackedDownloads.Should().HaveCount(1);
            trackedDownloads.First().RemoteAlbum.Should().BeNull();
        }

        private RemoteAlbum GivenGrabHistoryMapping()
        {
            GivenGrabHistory(true);

            var remoteAlbum = new RemoteAlbum
            {
                Artist = new Artist { Id = 5, ForeignArtistId = "artist-5" },
                Albums = new List<Album> { new Album { Id = 4 } }
            };

            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.Map(It.IsAny<ParsedAlbumInfo>(), It.IsAny<int>(), It.IsAny<IEnumerable<int>>()))
                  .Returns(remoteAlbum);

            return remoteAlbum;
        }

        private void GivenIncompleteImport(string statusMessages)
        {
            Mocker.GetMock<IDownloadHistoryService>()
                  .Setup(s => s.GetLatestDownloadHistoryItem("35238"))
                  .Returns(new DownloadHistory
                  {
                      EventType = DownloadHistoryEventType.DownloadImportIncomplete,
                      Data = new Dictionary<string, string> { { "statusMessages", statusMessages } }
                  });
        }

        [Test]
        public void should_show_the_messages_of_an_incomplete_import()
        {
            GivenGrabHistoryMapping();
            GivenIncompleteImport("[{\"title\":\"Track Title\",\"messages\":[\"Not imported\"]}]");

            var trackedDownload = GivenTrackedTorrent();

            trackedDownload.State.Should().Be(TrackedDownloadState.ImportFailed);
            trackedDownload.StatusMessages.Should().ContainSingle(m => m.Title == "Track Title" && m.Messages.Contains("Not imported"));
        }

        [TestCase("not json")]
        [TestCase("null")]
        public void should_still_track_a_download_whose_incomplete_import_messages_cannot_be_read(string statusMessages)
        {
            GivenGrabHistoryMapping();
            GivenIncompleteImport(statusMessages);

            var trackedDownload = GivenTrackedTorrent();

            trackedDownload.State.Should().Be(TrackedDownloadState.ImportFailed);
            trackedDownload.RemoteAlbum.Artist.Id.Should().Be(5);
        }

        [Test]
        public void should_keep_the_grab_history_mapping_when_a_tracked_download_is_remapped()
        {
            var remoteAlbum = GivenGrabHistoryMapping();

            GivenTrackedTorrent().RemoteAlbum.Artist.Id.Should().Be(5);

            Subject.Handle(new ArtistAddedEvent(remoteAlbum.Artist));

            var remapped = Subject.GetTrackedDownloads().Single().RemoteAlbum;

            remapped.Artist.Id.Should().Be(5);
            remapped.Albums.Should().ContainSingle(a => a.Id == 4);
        }

        [Test]
        public void should_keep_the_previous_mapping_when_the_title_lookup_fails_during_a_remap()
        {
            var remoteAlbum = new RemoteAlbum
            {
                Artist = new Artist { Id = 5, ForeignArtistId = "artist-5" },
                Albums = new List<Album> { new Album { Id = 4 } }
            };

            Mocker.GetMock<IHistoryService>()
                  .Setup(s => s.FindByDownloadId("35238"))
                  .Returns(new List<EntityHistory>());

            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.Map(It.IsAny<ParsedAlbumInfo>(), It.IsAny<SearchCriteriaBase>()))
                  .Returns(remoteAlbum);

            GivenTrackedTorrent("Audio Artist - Audio Album [2018 - FLAC]");

            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.Map(It.IsAny<ParsedAlbumInfo>(), It.IsAny<SearchCriteriaBase>()))
                  .Throws(new InvalidOperationException("Database is locked"));

            Subject.Handle(new ArtistAddedEvent(remoteAlbum.Artist));

            Subject.GetTrackedDownloads().Single().RemoteAlbum.Should().BeSameAs(remoteAlbum);
        }

        [Test]
        public void should_keep_the_previous_mapping_when_the_grab_history_read_fails_during_a_remap()
        {
            var remoteAlbum = GivenGrabHistoryMapping();

            GivenTrackedTorrent();

            Mocker.GetMock<IHistoryService>()
                  .Setup(s => s.FindByDownloadId("35238"))
                  .Throws(new InvalidOperationException("Database is locked"));

            Subject.Handle(new ArtistAddedEvent(remoteAlbum.Artist));

            Subject.GetTrackedDownloads().Single().RemoteAlbum.Should().BeSameAs(remoteAlbum);
        }

        [Test]
        public void should_map_a_new_download_whose_download_history_cannot_be_read()
        {
            var remoteAlbum = GivenGrabHistoryMapping();

            Mocker.GetMock<IDownloadHistoryService>()
                  .Setup(s => s.GetLatestDownloadHistoryItem("35238"))
                  .Throws(new InvalidOperationException("Database is locked"));

            var trackedDownload = GivenTrackedTorrent();

            trackedDownload.RemoteAlbum.Should().BeSameAs(remoteAlbum);
            trackedDownload.State.Should().Be(TrackedDownloadState.Downloading);
            Subject.GetTrackedDownloads().Should().ContainSingle();
        }

        [Test]
        public void should_not_point_at_a_deleted_artist_when_its_grab_history_remains()
        {
            var remoteAlbum = GivenGrabHistoryMapping();

            GivenTrackedTorrent();

            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.Map(It.IsAny<ParsedAlbumInfo>(), It.IsAny<int>(), It.IsAny<IEnumerable<int>>()))
                  .Throws(new ModelNotFoundException(typeof(Artist), 5));

            Subject.Handle(new ArtistsDeletedEvent(new List<Artist> { remoteAlbum.Artist }, true, true));

            Subject.GetTrackedDownloads().Single().RemoteAlbum.Should().BeNull();
        }

        [Test]
        public void should_unmap_a_download_whose_title_does_not_parse_once_its_grab_history_is_gone()
        {
            var remoteAlbum = GivenGrabHistoryMapping();

            Parser.Parser.ParseAlbumTitle("The torrent release folder").Should().BeNull();

            GivenTrackedTorrent();

            Mocker.GetMock<IHistoryService>()
                  .Setup(s => s.FindByDownloadId("35238"))
                  .Returns(new List<EntityHistory>());

            Subject.Handle(new ArtistsDeletedEvent(new List<Artist> { remoteAlbum.Artist }, true, true));

            Subject.GetTrackedDownloads().Single().RemoteAlbum.Should().BeNull();
        }

        [Test]
        public void should_not_show_a_partial_mapping_while_a_remap_runs()
        {
            var remoteAlbum = GivenGrabHistoryMapping();
            GivenTrackedTorrent();

            RemoteAlbum seen = null;

            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.Map(It.IsAny<ParsedAlbumInfo>(), It.IsAny<int>(), It.IsAny<IEnumerable<int>>()))
                  .Callback(() => seen = Subject.Find("35238").RemoteAlbum)
                  .Returns(new RemoteAlbum { Artist = remoteAlbum.Artist, Albums = remoteAlbum.Albums });

            Subject.Handle(new ArtistAddedEvent(remoteAlbum.Artist));

            seen.Should().BeSameAs(remoteAlbum);
        }

        [Test]
        public void should_keep_the_previous_mapping_and_messages_when_a_remap_fails_after_mapping()
        {
            var remoteAlbum = GivenGrabHistoryMapping();
            GivenIncompleteImport("[{\"title\":\"Track Title\",\"messages\":[\"Not imported\"]}]");

            var trackedDownload = GivenTrackedTorrent();

            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.Map(It.IsAny<ParsedAlbumInfo>(), It.IsAny<int>(), It.IsAny<IEnumerable<int>>()))
                  .Returns(new RemoteAlbum { Artist = remoteAlbum.Artist, Albums = new List<Album>() });
            Mocker.GetMock<IRemoteAlbumAggregationService>()
                  .Setup(s => s.Augment(It.IsAny<RemoteAlbum>()))
                  .Throws(new InvalidOperationException("Database is locked"));

            Subject.Handle(new ArtistAddedEvent(remoteAlbum.Artist));

            trackedDownload.RemoteAlbum.Should().BeSameAs(remoteAlbum);
            trackedDownload.Indexer.Should().Be("Test Indexer");
            trackedDownload.StatusMessages.Should().ContainSingle(m => m.Title == "Track Title");
        }

        [Test]
        public void should_track_a_new_download_with_a_warning_when_its_mapping_fails()
        {
            var remoteAlbum = GivenGrabHistoryMapping();

            Mocker.GetMock<IRemoteAlbumAggregationService>()
                  .Setup(s => s.Augment(It.IsAny<RemoteAlbum>()))
                  .Throws(new InvalidOperationException("Database is locked"));

            var trackedDownload = GivenTrackedTorrent();

            trackedDownload.RemoteAlbum.Should().BeSameAs(remoteAlbum);
            trackedDownload.Status.Should().Be(TrackedDownloadStatus.Warning);
            trackedDownload.StatusMessages.Should().ContainSingle(m => m.Messages.Contains("Unable to parse albums from title"));

            var remapped = new RemoteAlbum { Artist = remoteAlbum.Artist, Albums = remoteAlbum.Albums };
            trackedDownload.State = TrackedDownloadState.ImportPending;

            Mocker.GetMock<IRemoteAlbumAggregationService>()
                  .Setup(s => s.Augment(It.IsAny<RemoteAlbum>()))
                  .Returns<RemoteAlbum>(r => r);
            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.Map(It.IsAny<ParsedAlbumInfo>(), It.IsAny<int>(), It.IsAny<IEnumerable<int>>()))
                  .Returns(remapped);

            GivenTrackedTorrent();

            trackedDownload.RemoteAlbum.Should().BeSameAs(remapped);
        }

        [Test]
        public void should_retry_a_failed_remap_on_the_next_refresh_until_it_succeeds()
        {
            var remoteAlbum = GivenGrabHistoryMapping();
            var trackedDownload = GivenTrackedTorrent();
            trackedDownload.State = TrackedDownloadState.ImportPending;

            var history = Mocker.Resolve<IHistoryService>().FindByDownloadId("35238");
            var remapped = new RemoteAlbum { Artist = remoteAlbum.Artist, Albums = remoteAlbum.Albums };

            Mocker.GetMock<IHistoryService>()
                  .SetupSequence(s => s.FindByDownloadId("35238"))
                  .Throws(new InvalidOperationException("Database is locked"))
                  .Returns(history)
                  .Returns(history);
            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.Map(It.IsAny<ParsedAlbumInfo>(), It.IsAny<int>(), It.IsAny<IEnumerable<int>>()))
                  .Returns(remapped);

            Subject.Handle(new ArtistAddedEvent(remoteAlbum.Artist));

            trackedDownload.RemoteAlbum.Should().BeSameAs(remoteAlbum);

            GivenTrackedTorrent();

            trackedDownload.RemoteAlbum.Should().BeSameAs(remapped);

            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.Map(It.IsAny<ParsedAlbumInfo>(), It.IsAny<int>(), It.IsAny<IEnumerable<int>>()))
                  .Returns(new RemoteAlbum());

            GivenTrackedTorrent();

            trackedDownload.RemoteAlbum.Should().BeSameAs(remapped);
        }
    }
}
