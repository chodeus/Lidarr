using System;
using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.History;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.DecisionEngineTests
{
    [TestFixture]
    public class ImportedToDifferentAlbumHistoryFixture : DbTest<ImportedToDifferentAlbumSpecification, EntityHistory>
    {
        private const string TITLE = "Some.Artist-Some.Album-2018-320kbps-CD-Lidarr";
        private const string HASH = "0123456789ABCDEF0123456789ABCDEF01234567";

        private Artist _artist;
        private Artist _otherArtist;
        private Album _wanted;
        private Album _wrong;
        private Album _otherArtistAlbum;
        private EntityHistoryRepository _repo;

        [SetUp]
        public void Setup()
        {
            var artists = Builder<Artist>.CreateListOfSize(2).BuildListOfNew();
            _artist = Db.Insert(artists[0]);
            _otherArtist = Db.Insert(artists[1]);

            var albums = Builder<Album>.CreateListOfSize(3).BuildListOfNew();
            _wanted = Db.Insert(albums[0]);
            _wrong = Db.Insert(albums[1]);
            _otherArtistAlbum = Db.Insert(albums[2]);

            _repo = Mocker.Resolve<EntityHistoryRepository>();
            Mocker.SetConstant<IHistoryRepository>(_repo);
            Mocker.SetConstant<IHistoryService>(Mocker.Resolve<EntityHistoryService>());

            Mocker.GetMock<IConfigService>()
                  .SetupGet(s => s.EnableCompletedDownloadHandling)
                  .Returns(true);
        }

        private void GivenEvent(EntityHistoryEventType type, int artistId, int albumId, DateTime date)
        {
            _repo.Insert(new EntityHistory
            {
                EventType = type,
                Date = date,
                Quality = new QualityModel(Quality.MP3_320),
                SourceTitle = TITLE,
                ArtistId = artistId,
                AlbumId = albumId,
                DownloadId = HASH
            });
        }

        private RemoteAlbum GivenTorrentRelease()
        {
            return new RemoteAlbum
            {
                Artist = _artist,
                Albums = new List<Album> { _wanted },
                Release = Builder<TorrentInfo>.CreateNew()
                                              .With(t => t.DownloadProtocol = nameof(TorrentDownloadProtocol))
                                              .With(t => t.InfoHash = HASH.ToLowerInvariant())
                                              .With(t => t.Title = TITLE)
                                              .Build()
            };
        }

        [Test]
        public void should_reject_a_release_last_imported_into_another_album()
        {
            var grab = DateTime.UtcNow.AddDays(-2);
            GivenEvent(EntityHistoryEventType.Grabbed, _artist.Id, _wanted.Id, grab);
            GivenEvent(EntityHistoryEventType.TrackFileImported, _artist.Id, _wrong.Id, grab.AddHours(1));

            Subject.IsSatisfiedBy(GivenTorrentRelease(), null).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_reject_a_release_last_imported_into_another_artists_album()
        {
            var grab = DateTime.UtcNow.AddDays(-2);
            GivenEvent(EntityHistoryEventType.Grabbed, _artist.Id, _wanted.Id, grab);
            GivenEvent(EntityHistoryEventType.TrackFileImported, _otherArtist.Id, _otherArtistAlbum.Id, grab.AddHours(1));

            Subject.IsSatisfiedBy(GivenTorrentRelease(), null).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_reject_when_an_old_grab_of_the_same_torrent_was_for_the_album_it_now_imports_into()
        {
            var oldGrab = DateTime.UtcNow.AddDays(-60);
            GivenEvent(EntityHistoryEventType.Grabbed, _artist.Id, _wrong.Id, oldGrab);
            GivenEvent(EntityHistoryEventType.TrackFileImported, _artist.Id, _wrong.Id, oldGrab.AddHours(1));

            var grab = DateTime.UtcNow.AddDays(-2);
            GivenEvent(EntityHistoryEventType.Grabbed, _artist.Id, _wanted.Id, grab);
            GivenEvent(EntityHistoryEventType.TrackFileImported, _artist.Id, _wrong.Id, grab.AddHours(1));

            Subject.IsSatisfiedBy(GivenTorrentRelease(), null).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_accept_a_grab_for_several_albums_that_imported_into_one_of_them()
        {
            var grab = DateTime.UtcNow.AddDays(-2);
            GivenEvent(EntityHistoryEventType.Grabbed, _artist.Id, _wrong.Id, grab);
            GivenEvent(EntityHistoryEventType.Grabbed, _artist.Id, _wanted.Id, grab.AddSeconds(2));
            GivenEvent(EntityHistoryEventType.TrackFileImported, _artist.Id, _wrong.Id, grab.AddHours(1));

            Subject.IsSatisfiedBy(GivenTorrentRelease(), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_reject_when_a_regrab_half_a_minute_earlier_was_for_the_album_it_imports_into()
        {
            var grab = DateTime.UtcNow.AddDays(-2);
            GivenEvent(EntityHistoryEventType.Grabbed, _artist.Id, _wrong.Id, grab.AddSeconds(-30));
            GivenEvent(EntityHistoryEventType.Grabbed, _artist.Id, _wanted.Id, grab);
            GivenEvent(EntityHistoryEventType.TrackFileImported, _artist.Id, _wrong.Id, grab.AddHours(1));

            Subject.IsSatisfiedBy(GivenTorrentRelease(), null).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_reject_when_the_wrong_album_no_longer_exists_before_housekeeping()
        {
            var grab = DateTime.UtcNow.AddDays(-2);
            GivenEvent(EntityHistoryEventType.Grabbed, _artist.Id, _wanted.Id, grab);
            GivenEvent(EntityHistoryEventType.TrackFileImported, _artist.Id, _wrong.Id, grab.AddHours(1));

            Db.Delete(_wrong);

            Subject.IsSatisfiedBy(GivenTorrentRelease(), null).Accepted.Should().BeFalse();
        }
    }
}
