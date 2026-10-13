using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.History;
using NzbDrone.Core.Music;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.HistoryTests
{
    [TestFixture]
    public class HistoryRepositoryFixture : DbTest<EntityHistoryRepository, EntityHistory>
    {
        [Test]
        public void should_read_write_dictionary()
        {
            var history = Builder<EntityHistory>.CreateNew()
                .With(c => c.Quality = new QualityModel())
                .BuildNew();

            history.Data.Add("key1", "value1");
            history.Data.Add("key2", "value2");

            Subject.Insert(history);

            StoredModel.Data.Should().HaveCount(2);
        }

        [Test]
        public void should_get_download_history()
        {
            var historyBluray = Builder<EntityHistory>.CreateNew()
                .With(c => c.Quality = new QualityModel(Quality.MP3_320))
                .With(c => c.ArtistId = 12)
                .With(c => c.EventType = EntityHistoryEventType.Grabbed)
                .BuildNew();

            var historyDvd = Builder<EntityHistory>.CreateNew()
                .With(c => c.Quality = new QualityModel(Quality.MP3_192))
                .With(c => c.ArtistId = 12)
                .With(c => c.EventType = EntityHistoryEventType.Grabbed)
             .BuildNew();

            Subject.Insert(historyBluray);
            Subject.Insert(historyDvd);

            var downloadHistory = Subject.FindDownloadHistory(12, new QualityModel(Quality.MP3_320));

            downloadHistory.Should().HaveCount(1);
        }

        [Test]
        public void should_count_paged_history_the_same_as_its_records()
        {
            var artist = Db.Insert(Builder<Artist>.CreateNew().With(a => a.ArtistMetadataId = 1).BuildNew());
            var album = Db.Insert(Builder<Album>.CreateNew().With(a => a.ArtistMetadataId = 1).BuildNew());
            var track = Db.Insert(Builder<Track>.CreateNew().BuildNew());

            var rows = new[]
            {
                (AlbumId: album.Id, TrackId: 0, Quality: Quality.MP3_320),
                (AlbumId: album.Id, TrackId: track.Id, Quality: Quality.MP3_320),
                (AlbumId: album.Id, TrackId: track.Id + 100, Quality: Quality.FLAC),
                (AlbumId: album.Id + 100, TrackId: 0, Quality: Quality.MP3_320)
            };

            foreach (var row in rows)
            {
                Subject.Insert(Builder<EntityHistory>.CreateNew()
                    .With(h => h.ArtistId = artist.Id)
                    .With(h => h.AlbumId = row.AlbumId)
                    .With(h => h.TrackId = row.TrackId)
                    .With(h => h.Quality = new QualityModel(row.Quality))
                    .BuildNew());
            }

            var all = Subject.GetPaged(new PagingSpec<EntityHistory> { Page = 1, PageSize = 10 }, null);
            var mp3 = Subject.GetPaged(new PagingSpec<EntityHistory> { Page = 1, PageSize = 10 }, new[] { Quality.MP3_320.Id });

            all.Records.Should().HaveCount(3);
            all.TotalRecords.Should().Be(3);
            mp3.Records.Should().HaveCount(2);
            mp3.TotalRecords.Should().Be(2);
        }
    }
}
