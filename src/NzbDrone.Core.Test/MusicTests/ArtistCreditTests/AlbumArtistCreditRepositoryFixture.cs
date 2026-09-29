using System;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Music;
using NzbDrone.Core.Music.ArtistCredits;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MusicTests.ArtistCreditTests
{
    [TestFixture]
    public class AlbumArtistCreditRepositoryFixture : DbTest<AlbumArtistCreditRepository, AlbumArtistCredit>
    {
        private AlbumRepository _albums;
        private int _metadataId;

        [SetUp]
        public void Setup()
        {
            _albums = Mocker.Resolve<AlbumRepository>();
            _metadataId = Mocker.Resolve<ArtistMetadataRepository>()
                .Insert(new ArtistMetadata { ForeignArtistId = "primary-id", Name = "Artist Name" }).Id;
        }

        private Album GivenAlbum(string title, int addedYear)
        {
            return _albums.Insert(new Album
            {
                Title = title,
                CleanTitle = title.ToLowerInvariant(),
                ForeignAlbumId = Guid.NewGuid().ToString(),
                ArtistMetadataId = _metadataId,
                AlbumType = "Single",
                Added = new DateTime(addedYear, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            });
        }

        private void GivenCredit(Album album, int daysAgo)
        {
            Subject.Insert(new AlbumArtistCredit { AlbumId = album.Id, Credit = "Artist Name", Guests = "", LastFetched = DateTime.UtcNow.AddDays(-daysAgo) });
        }

        [Test]
        public void should_order_missing_same_titled_then_missing_then_stale()
        {
            var older = GivenAlbum("Song", 2020);
            var newer = GivenAlbum("Song", 2021);
            var unique = GivenAlbum("Other Song", 2022);
            var stale = GivenAlbum("Stale Song", 2019);
            var fresh = GivenAlbum("Fresh Song", 2019);
            GivenCredit(stale, 200);
            GivenCredit(fresh, 1);

            var candidates = Subject.GetCandidates(DateTime.UtcNow.AddDays(-90), 10);

            candidates.Select(c => c.AlbumId).Should().Equal(newer.Id, older.Id, unique.Id, stale.Id);
            candidates.Should().OnlyContain(c => c.ForeignArtistId == "primary-id");
        }

        private void GivenFile(Album album)
        {
            Db.Insert(Builder<TrackFile>.CreateNew()
                .With(f => f.Id = 0)
                .With(f => f.AlbumId = album.Id)
                .With(f => f.Quality = new QualityModel(Quality.FLAC))
                .With(f => f.Path = $"/music/Artist Name/{album.Title}/01 - Track Title.flac")
                .Build());
        }

        [Test]
        public void should_put_albums_with_files_before_same_titled_and_newer_ones()
        {
            var newest = GivenAlbum("Newest Song", 2022);
            var withFile = GivenAlbum("Song With File", 2018);
            var pairNewer = GivenAlbum("Song", 2021);
            var pairOlder = GivenAlbum("Song", 2019);
            GivenFile(withFile);

            var candidates = Subject.GetCandidates(DateTime.UtcNow.AddDays(-90), 10);

            candidates.Select(c => c.AlbumId).Should().Equal(withFile.Id, pairNewer.Id, pairOlder.Id, newest.Id);
        }

        [Test]
        public void should_respect_the_limit()
        {
            GivenAlbum("Song One", 2020);
            GivenAlbum("Song Two", 2021);
            GivenAlbum("Song Three", 2022);

            Subject.GetCandidates(DateTime.UtcNow.AddDays(-90), 2).Should().HaveCount(2);
        }

        [Test]
        public void should_find_credits_for_a_large_id_list()
        {
            var album = GivenAlbum("Song", 2020);
            GivenCredit(album, 1);
            var ids = Enumerable.Range(100000, 33000).Append(album.Id).ToList();

            Subject.FindByAlbumIds(ids).Should().ContainSingle(c => c.AlbumId == album.Id);
        }

        [Test]
        public void should_find_and_delete_by_album()
        {
            var album = GivenAlbum("Song", 2020);
            GivenCredit(album, 1);

            Subject.FindByAlbumId(album.Id).Should().NotBeNull();

            Subject.DeleteByAlbumId(album.Id);

            Subject.FindByAlbumId(album.Id).Should().BeNull();
        }
    }
}
