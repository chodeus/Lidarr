using System;
using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Core.Download;
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

            _album = Builder<Album>.CreateNew()
                                  .With(a => a.Id = 0)
                                  .With(a => a.ArtistMetadataId = metadata.Id)
                                  .With(a => a.ForeignAlbumId = "album-1")
                                  .With(a => a.Title = "Audio Album")
                                  .With(a => a.CleanTitle = "audioalbum")
                                  .With(a => a.ReleaseDate = new DateTime(2018, 1, 1))
                                  .Build();
            Db.Insert(_album);

            Mocker.GetMock<IHistoryService>()
                  .Setup(s => s.FindByDownloadId("35238"))
                  .Returns(new List<EntityHistory>
                  {
                      new EntityHistory
                      {
                          DownloadId = "35238",
                          EventType = EntityHistoryEventType.Grabbed,
                          SourceTitle = "Audio Artist - Audio Album [2018 - FLAC]",
                          ArtistId = _artist.Id,
                          AlbumId = _album.Id,
                          Data = new Dictionary<string, string> { { "indexer", "Test Indexer" } }
                      }
                  });
        }

        private TrackedDownload GivenTrackedTorrent(string title = "The torrent release folder")
        {
            var client = new DownloadClientDefinition { Id = 1, Protocol = nameof(TorrentDownloadProtocol) };
            var item = new DownloadClientItem
            {
                Title = title,
                DownloadId = "35238",
                DownloadClientInfo = new DownloadClientItemClientInfo { Protocol = client.Protocol, Id = client.Id, Name = client.Name }
            };

            var trackedDownload = Subject.TrackDownload(client, item);

            trackedDownload.RemoteAlbum.Albums.Should().ContainSingle(a => a.Id == _album.Id);

            return trackedDownload;
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
        public void should_not_keep_a_deleted_artist_whose_grab_history_remains()
        {
            var trackedDownload = GivenTrackedTorrent();

            Mocker.Resolve<IArtistService>().DeleteArtist(_artist.Id, false);
            Subject.Handle(new ArtistsDeletedEvent(new List<Artist> { _artist }, false, false));

            trackedDownload.RemoteAlbum?.Artist.Should().BeNull();
            Subject.GetTrackedDownloads().Should().ContainSingle();
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
    }
}
