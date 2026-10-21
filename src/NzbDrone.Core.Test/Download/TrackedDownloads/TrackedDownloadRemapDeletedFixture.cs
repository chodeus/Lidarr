using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Music;
using NzbDrone.Core.Music.Events;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download.TrackedDownloads
{
    // The real parsing, artist and album services over the test database; the history is mocked
    [TestFixture]
    public class TrackedDownloadRemapDeletedFixture : DbTest<TrackedDownloadService, Album>
    {
        private Artist _artist;
        private Album _album;
        private Album _secondAlbum;
        private List<EntityHistory> _history;

        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<ICacheManager>(new CacheManager());
            Mocker.SetConstant<IArtistRepository>(Mocker.Resolve<ArtistRepository>());
            Mocker.SetConstant<IAlbumRepository>(Mocker.Resolve<AlbumRepository>());
            Mocker.SetConstant<IAlbumYearMatcher>(Mocker.Resolve<AlbumYearMatcher>());
            Mocker.SetConstant<IArtistService>(Mocker.Resolve<ArtistService>());
            Mocker.SetConstant<IAlbumService>(Mocker.Resolve<AlbumService>());
            Mocker.SetConstant<IParsingService>(Mocker.Resolve<ParsingService>());

            var metadata = Builder<ArtistMetadata>.CreateNew().With(m => m.Id = 0).With(m => m.ForeignArtistId = "artist-1").Build();
            Db.Insert(metadata);

            _artist = Builder<Artist>.CreateNew().With(a => a.Id = 0).With(a => a.ArtistMetadataId = metadata.Id).With(a => a.CleanName = "audioartist").Build();
            Db.Insert(_artist);

            _album = GivenAlbum(metadata.Id, "album-1", "Audio Album", 2018);
            _secondAlbum = GivenAlbum(metadata.Id, "album-2", "Second Album", 2019);

            _history = new List<EntityHistory> { GivenGrab(_album.Id) };

            Mocker.GetMock<IHistoryService>()
                  .Setup(s => s.FindByDownloadId("35238"))
                  .Returns(() => _history);
        }

        private Album GivenAlbum(int artistMetadataId, string foreignAlbumId, string title, int year)
        {
            var album = Builder<Album>.CreateNew()
                                      .With(a => a.Id = 0)
                                      .With(a => a.ArtistMetadataId = artistMetadataId)
                                      .With(a => a.ForeignAlbumId = foreignAlbumId)
                                      .With(a => a.Title = title)
                                      .With(a => a.CleanTitle = title.CleanArtistName())
                                      .With(a => a.ReleaseDate = new DateTime(year, 1, 1))
                                      .Build();

            Db.Insert(album);

            return album;
        }

        private EntityHistory GivenGrab(int albumId)
        {
            return new EntityHistory
            {
                DownloadId = "35238",
                EventType = EntityHistoryEventType.Grabbed,
                SourceTitle = "Audio Artist - Audio Album [2018 - FLAC]",
                ArtistId = _artist.Id,
                AlbumId = albumId,
                Data = new Dictionary<string, string> { { "indexer", "Test Indexer" } }
            };
        }

        private TrackedDownload Track(string title)
        {
            var client = new DownloadClientDefinition { Id = 1, Protocol = nameof(TorrentDownloadProtocol) };
            var item = new DownloadClientItem
            {
                Title = title,
                DownloadId = "35238",
                DownloadClientInfo = new DownloadClientItemClientInfo { Protocol = client.Protocol, Id = client.Id, Name = client.Name }
            };

            return Subject.TrackDownload(client, item);
        }

        private TrackedDownload GivenTrackedTorrent(string title = "The torrent release folder")
        {
            var trackedDownload = Track(title);

            trackedDownload.RemoteAlbum.Albums.Should().Contain(a => a.Id == _album.Id);

            return trackedDownload;
        }

        private void DeleteArtist()
        {
            Mocker.Resolve<IArtistService>().DeleteArtist(_artist.Id, false);
            Subject.Handle(new ArtistsDeletedEvent(new List<Artist> { _artist }, false, false));
        }

        [Test]
        public void should_not_keep_a_deleted_album_whose_grab_history_remains()
        {
            var trackedDownload = GivenTrackedTorrent();

            Mocker.Resolve<IAlbumService>().DeleteAlbum(_album.Id, false);
            Subject.Handle(new AlbumDeletedEvent(_album, false, false));

            (trackedDownload.RemoteAlbum?.Albums ?? new List<Album>()).Should().NotContain(a => a.Id == _album.Id);
        }

        [Test]
        public void should_keep_the_grabbed_albums_that_were_not_deleted()
        {
            _history = new List<EntityHistory> { GivenGrab(_album.Id), GivenGrab(_secondAlbum.Id) };
            var trackedDownload = GivenTrackedTorrent();

            Mocker.Resolve<IAlbumService>().DeleteAlbum(_album.Id, false);
            Subject.Handle(new AlbumDeletedEvent(_album, false, false));

            trackedDownload.RemoteAlbum.Artist.Id.Should().Be(_artist.Id);
            trackedDownload.RemoteAlbum.Albums.Should().ContainSingle(a => a.Id == _secondAlbum.Id);
        }

        [Test]
        public void should_keep_the_artist_the_title_names_when_its_grabbed_album_is_deleted()
        {
            var trackedDownload = GivenTrackedTorrent("Audio Artist - Audio Album [2018 - FLAC]");

            Mocker.Resolve<IAlbumService>().DeleteAlbum(_album.Id, false);
            Subject.Handle(new AlbumDeletedEvent(_album, false, false));

            trackedDownload.RemoteAlbum.Artist.Id.Should().Be(_artist.Id);
            trackedDownload.RemoteAlbum.Albums.Should().BeEmpty();
        }

        [Test]
        public void should_track_the_artist_the_title_names_when_its_grabbed_album_was_deleted_before()
        {
            Mocker.Resolve<IAlbumService>().DeleteAlbum(_album.Id, false);

            var trackedDownload = Track("Audio Artist - Audio Album [2018 - FLAC]");

            trackedDownload.RemoteAlbum.Artist.Id.Should().Be(_artist.Id);
            trackedDownload.RemoteAlbum.Albums.Should().BeEmpty();
            trackedDownload.Status.Should().Be(TrackedDownloadStatus.Ok);
        }

        [Test]
        public void should_not_keep_a_deleted_artist_whose_grab_history_remains()
        {
            var trackedDownload = GivenTrackedTorrent();

            DeleteArtist();

            trackedDownload.RemoteAlbum?.Artist.Should().BeNull();
            Subject.GetTrackedDownloads().Should().ContainSingle();
        }

        [Test]
        public void should_unmap_a_download_whose_remap_fails_after_its_artist_is_deleted()
        {
            var trackedDownload = GivenTrackedTorrent();
            trackedDownload.State = TrackedDownloadState.ImportPending;

            Mocker.GetMock<IHistoryService>()
                  .Setup(s => s.FindByDownloadId("35238"))
                  .Throws(new InvalidOperationException("Database is locked"));

            DeleteArtist();

            trackedDownload.RemoteAlbum.Should().BeNull();
            trackedDownload.Status.Should().Be(TrackedDownloadStatus.Warning);
        }

        [Test]
        public void should_not_cache_a_deleted_artist_when_tracking_races_its_delete()
        {
            GivenTrackedTorrent();

            Task delete = null;

            Mocker.GetMock<IDownloadHistoryService>()
                  .Setup(s => s.GetLatestGrab("35238"))
                  .Callback(() =>
                  {
                      if (delete != null)
                      {
                          return;
                      }

                      // The delete runs on another thread while this mapping holds what it read; one that waits for it times out here
                      delete = Task.Run(DeleteArtist);
                      delete.Wait(TimeSpan.FromSeconds(1));
                  })
                  .Returns((DownloadHistory)null);

            GivenTrackedTorrent();
            delete.Wait();

            Subject.Find("35238").RemoteAlbum?.Artist.Should().BeNull();
        }
    }
}
