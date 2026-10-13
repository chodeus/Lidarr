using System;
using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Music;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MusicTests.AlbumRepositoryTests
{
    [TestFixture]
    public class AlbumsWantedFixture : DbTest<AlbumRepository, Album>
    {
        private int _next;
        private Artist _artist;

        [SetUp]
        public void Setup()
        {
            _artist = GivenArtist(1);
        }

        private string Unique(string prefix)
        {
            return $"{prefix} {++_next}";
        }

        private Artist GivenArtist(int qualityProfileId)
        {
            var metadata = Builder<ArtistMetadata>.CreateNew()
                .With(m => m.ForeignArtistId = Unique("Foreign Artist"))
                .With(m => m.Name = "Artist Name")
                .BuildNew();
            Db.Insert(metadata);

            var artist = Builder<Artist>.CreateNew()
                .With(a => a.ArtistMetadataId = metadata.Id)
                .With(a => a.QualityProfileId = qualityProfileId)
                .With(a => a.Monitored = true)
                .BuildNew();

            return Db.Insert(artist);
        }

        private Album GivenAlbum(Artist artist, int releasedDaysAgo = 30, bool monitored = true)
        {
            var album = Builder<Album>.CreateNew()
                .With(a => a.ForeignAlbumId = Unique("Foreign Album"))
                .With(a => a.Title = "Album Title")
                .With(a => a.ArtistMetadataId = artist.ArtistMetadataId)
                .With(a => a.ReleaseDate = DateTime.UtcNow.AddDays(-releasedDaysAgo))
                .With(a => a.Monitored = monitored)
                .BuildNew();

            return Db.Insert(album);
        }

        private AlbumRelease GivenRelease(Album album, bool monitored = true)
        {
            var release = Builder<AlbumRelease>.CreateNew()
                .With(r => r.ForeignReleaseId = Unique("Foreign Release"))
                .With(r => r.AlbumId = album.Id)
                .With(r => r.Monitored = monitored)
                .BuildNew();

            return Db.Insert(release);
        }

        private int GivenTrackFile(Album album, Quality quality)
        {
            var trackFile = Builder<TrackFile>.CreateNew()
                .With(f => f.AlbumId = album.Id)
                .With(f => f.Path = Unique("/music/Artist Name/Album Title/Track"))
                .With(f => f.Quality = new QualityModel(quality))
                .BuildNew();

            return Db.Insert(trackFile).Id;
        }

        private void GivenTracks(AlbumRelease release, params int[] trackFileIds)
        {
            foreach (var trackFileId in trackFileIds)
            {
                var track = Builder<Track>.CreateNew()
                    .With(t => t.ForeignTrackId = Unique("Foreign Track"))
                    .With(t => t.ForeignRecordingId = Unique("Foreign Recording"))
                    .With(t => t.AlbumReleaseId = release.Id)
                    .With(t => t.ArtistMetadataId = _artist.ArtistMetadataId)
                    .With(t => t.TrackFileId = trackFileId)
                    .BuildNew();

                Db.Insert(track);
            }
        }

        private Album GivenAlbumWithFiles(Artist artist, Quality quality)
        {
            var album = GivenAlbum(artist);
            GivenTracks(GivenRelease(album), GivenTrackFile(album, quality), GivenTrackFile(album, quality));

            return album;
        }

        private static PagingSpec<Album> Page(bool monitoredOnly = false)
        {
            var pagingSpec = new PagingSpec<Album> { Page = 1, PageSize = 10, SortDirection = SortDirection.Ascending };

            if (monitoredOnly)
            {
                pagingSpec.FilterExpressions.Add(v => v.Monitored == true && v.Artist.Value.Monitored == true);
            }

            return pagingSpec;
        }

        private static List<QualitiesBelowCutoff> BelowCutoff()
        {
            return new List<QualitiesBelowCutoff> { new QualitiesBelowCutoff(1, new[] { Quality.MP3_256.Id }) };
        }

        [Test]
        public void missing_should_return_albums_with_a_track_missing_its_file()
        {
            GivenAlbumWithFiles(_artist, Quality.FLAC);

            var partial = GivenAlbum(_artist);
            var partialRelease = GivenRelease(partial);
            GivenTracks(partialRelease, GivenTrackFile(partial, Quality.FLAC), 0);

            var empty = GivenAlbum(_artist);
            GivenTracks(GivenRelease(empty), 0, 0, 0);

            var result = Subject.AlbumsWithoutFiles(Page());

            result.Records.Select(a => a.Id).Should().BeEquivalentTo(new[] { partial.Id, empty.Id });
            result.TotalRecords.Should().Be(2);
        }

        [Test]
        public void missing_should_return_albums_whose_track_points_at_a_deleted_file()
        {
            var album = GivenAlbum(_artist);
            GivenTracks(GivenRelease(album), GivenTrackFile(album, Quality.FLAC), 999);

            var result = Subject.AlbumsWithoutFiles(Page());

            result.Records.Select(a => a.Id).Should().Equal(album.Id);
            result.TotalRecords.Should().Be(1);
        }

        [Test]
        public void missing_should_ignore_missing_tracks_on_an_unmonitored_release()
        {
            var album = GivenAlbumWithFiles(_artist, Quality.FLAC);
            GivenTracks(GivenRelease(album, monitored: false), 0, 0);

            var result = Subject.AlbumsWithoutFiles(Page());

            result.Records.Should().BeEmpty();
            result.TotalRecords.Should().Be(0);
        }

        [Test]
        public void missing_should_ignore_albums_not_released_yet()
        {
            var album = GivenAlbum(_artist, releasedDaysAgo: -30);
            GivenTracks(GivenRelease(album), 0, 0);

            var result = Subject.AlbumsWithoutFiles(Page());

            result.Records.Should().BeEmpty();
            result.TotalRecords.Should().Be(0);
        }

        [Test]
        public void missing_should_return_each_album_once_and_count_albums_not_tracks()
        {
            for (var i = 0; i < 3; i++)
            {
                var album = GivenAlbum(_artist);
                GivenTracks(GivenRelease(album), 0, 0, 0, 0);
            }

            var result = Subject.AlbumsWithoutFiles(Page());

            result.Records.Should().HaveCount(3);
            result.Records.Select(a => a.Id).Should().OnlyHaveUniqueItems();
            result.TotalRecords.Should().Be(3);
        }

        [Test]
        public void missing_should_apply_the_monitored_filter_to_records_and_count()
        {
            var monitored = GivenAlbum(_artist);
            GivenTracks(GivenRelease(monitored), 0);

            var unmonitored = GivenAlbum(_artist, monitored: false);
            GivenTracks(GivenRelease(unmonitored), 0);

            var result = Subject.AlbumsWithoutFiles(Page(monitoredOnly: true));

            result.Records.Select(a => a.Id).Should().Equal(monitored.Id);
            result.TotalRecords.Should().Be(1);
        }

        [Test]
        public void cutoff_should_return_albums_with_a_file_below_the_cutoff()
        {
            GivenAlbumWithFiles(_artist, Quality.FLAC);

            var mixed = GivenAlbum(_artist);
            GivenTracks(GivenRelease(mixed), GivenTrackFile(mixed, Quality.FLAC), GivenTrackFile(mixed, Quality.MP3_256));

            var lossy = GivenAlbumWithFiles(_artist, Quality.MP3_256);

            var result = Subject.AlbumsWhereCutoffUnmet(Page(), BelowCutoff());

            result.Records.Select(a => a.Id).Should().BeEquivalentTo(new[] { mixed.Id, lossy.Id });
            result.TotalRecords.Should().Be(2);
        }

        [Test]
        public void cutoff_should_ignore_files_on_an_unmonitored_release()
        {
            var album = GivenAlbumWithFiles(_artist, Quality.FLAC);
            GivenTracks(GivenRelease(album, monitored: false), GivenTrackFile(album, Quality.MP3_256));

            var result = Subject.AlbumsWhereCutoffUnmet(Page(), BelowCutoff());

            result.Records.Should().BeEmpty();
            result.TotalRecords.Should().Be(0);
        }

        [Test]
        public void cutoff_should_only_match_the_artists_own_quality_profile()
        {
            GivenAlbumWithFiles(GivenArtist(2), Quality.MP3_256);

            var result = Subject.AlbumsWhereCutoffUnmet(Page(), BelowCutoff());

            result.Records.Should().BeEmpty();
            result.TotalRecords.Should().Be(0);
        }
    }
}
