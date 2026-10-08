using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Download;
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
                .Returns(new DownloadHistory { EventType = DownloadHistoryEventType.FileImported, IndexerId = 0 });

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
    }
}
