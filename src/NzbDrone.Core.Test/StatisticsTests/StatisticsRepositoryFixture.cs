using System;
using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.ArtistStats;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Music;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Statistics;
using NzbDrone.Core.Tags;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.StatisticsTests
{
    [TestFixture]
    public class StatisticsRepositoryFixture : DbTest<StatisticsRepository, Artist>
    {
        private int _nextId = 1;
        private DateTime _released;
        private DateTime _unreleased;

        [SetUp]
        public void Setup()
        {
            _released = DateTime.UtcNow.AddDays(-5);
            _unreleased = DateTime.UtcNow.AddDays(5);
        }

        private Artist GivenArtist(bool monitored = true,
                                   ArtistStatusType status = ArtistStatusType.Continuing,
                                   int qualityProfileId = 1,
                                   int metadataProfileId = 1,
                                   string path = null,
                                   HashSet<int> tags = null)
        {
            var id = _nextId++;

            var metadata = Db.Insert(Builder<ArtistMetadata>.CreateNew()
                                                            .With(m => m.ForeignArtistId = $"artist-{id}")
                                                            .With(m => m.Status = status)
                                                            .BuildNew());

            return Db.Insert(Builder<Artist>.CreateNew()
                                            .With(a => a.ArtistMetadataId = metadata.Id)
                                            .With(a => a.CleanName = $"artist{id}")
                                            .With(a => a.Monitored = monitored)
                                            .With(a => a.QualityProfileId = qualityProfileId)
                                            .With(a => a.MetadataProfileId = metadataProfileId)
                                            .With(a => a.Path = (path ?? $"/music/Artist {id}").AsOsAgnostic())
                                            .With(a => a.Tags = tags ?? new HashSet<int>())
                                            .BuildNew());
        }

        private Album GivenAlbum(Artist artist, DateTime? releaseDate, bool monitored = true)
        {
            var id = _nextId++;

            return Db.Insert(Builder<Album>.CreateNew()
                                           .With(a => a.ArtistMetadataId = artist.ArtistMetadataId)
                                           .With(a => a.ForeignAlbumId = $"album-{id}")
                                           .With(a => a.CleanTitle = $"album{id}")
                                           .With(a => a.ReleaseDate = releaseDate)
                                           .With(a => a.Monitored = monitored)
                                           .BuildNew());
        }

        private AlbumRelease GivenRelease(Album album, bool monitored = true)
        {
            return Db.Insert(Builder<AlbumRelease>.CreateNew()
                                                  .With(r => r.AlbumId = album.Id)
                                                  .With(r => r.ForeignReleaseId = $"release-{_nextId++}")
                                                  .With(r => r.Monitored = monitored)
                                                  .BuildNew());
        }

        private void GivenTrack(AlbumRelease release, Artist artist, int trackFileId = 0)
        {
            var id = _nextId++;

            Db.Insert(Builder<Track>.CreateNew()
                                    .With(t => t.AlbumReleaseId = release.Id)
                                    .With(t => t.ArtistMetadataId = artist.ArtistMetadataId)
                                    .With(t => t.ForeignTrackId = $"track-{id}")
                                    .With(t => t.ForeignRecordingId = $"recording-{id}")
                                    .With(t => t.TrackFileId = trackFileId)
                                    .BuildNew());
        }

        private TrackFile GivenTrackFile(int albumId, long size = 100, Quality quality = null)
        {
            return Db.Insert(Builder<TrackFile>.CreateNew()
                                               .With(f => f.AlbumId = albumId)
                                               .With(f => f.Path = $"/music/Track {_nextId++}.flac".AsOsAgnostic())
                                               .With(f => f.Size = size)
                                               .With(f => f.Quality = new QualityModel(quality ?? Quality.FLAC))
                                               .BuildNew());
        }

        // An album whose monitored release has trackCount tracks, the first fileCount of them with a 100 byte file each
        private Album GivenAlbumWithTracks(Artist artist, DateTime? releaseDate, int trackCount, int fileCount = 0, bool monitored = true)
        {
            var album = GivenAlbum(artist, releaseDate, monitored);
            var release = GivenRelease(album);

            for (var i = 0; i < trackCount; i++)
            {
                GivenTrack(release, artist, i < fileCount ? GivenTrackFile(album.Id).Id : 0);
            }

            return album;
        }

        private Tag GivenTag(string label)
        {
            return Db.Insert(new Tag { Label = label });
        }

        [Test]
        public void should_return_zeros_when_library_is_empty()
        {
            var stats = Subject.GetLibraryStatistics();

            stats.ArtistCount.Should().Be(0);
            stats.MonitoredArtistCount.Should().Be(0);
            stats.CompletedArtistCount.Should().Be(0);
            stats.ContinuingArtistCount.Should().Be(0);
            stats.EndedArtistCount.Should().Be(0);
            stats.DeletedArtistCount.Should().Be(0);
            stats.AlbumCount.Should().Be(0);
            stats.MonitoredAlbumCount.Should().Be(0);
            stats.CompletedAlbumCount.Should().Be(0);
            stats.MissingAlbumCount.Should().Be(0);
            stats.TotalTrackCount.Should().Be(0);
            stats.MonitoredTrackCount.Should().Be(0);
            stats.DownloadedTrackCount.Should().Be(0);
            stats.MissingTrackCount.Should().Be(0);
            stats.UnreleasedTrackCount.Should().Be(0);
            stats.TrackFileCount.Should().Be(0);
            stats.SizeOnDisk.Should().Be(0);
        }

        [Test]
        public void should_count_artists_by_monitored_and_status()
        {
            GivenArtist(monitored: true, status: ArtistStatusType.Continuing);
            GivenArtist(monitored: true, status: ArtistStatusType.Continuing);
            GivenArtist(monitored: false, status: ArtistStatusType.Ended);
            GivenArtist(monitored: true, status: ArtistStatusType.Deleted);

            var stats = Subject.GetLibraryStatistics();

            stats.ArtistCount.Should().Be(4);
            stats.MonitoredArtistCount.Should().Be(3);
            stats.ContinuingArtistCount.Should().Be(2);
            stats.EndedArtistCount.Should().Be(1);
            stats.DeletedArtistCount.Should().Be(1);
        }

        [Test]
        public void should_count_completed_artists()
        {
            var complete = GivenArtist();
            GivenAlbumWithTracks(complete, _released, trackCount: 2, fileCount: 2);
            GivenAlbumWithTracks(complete, _unreleased, trackCount: 1);

            var incomplete = GivenArtist();
            GivenAlbumWithTracks(incomplete, _released, trackCount: 2, fileCount: 1);

            GivenArtist();

            var stats = Subject.GetLibraryStatistics();

            stats.ArtistCount.Should().Be(3);
            stats.CompletedArtistCount.Should().Be(1);
        }

        [Test]
        public void should_count_albums_and_completed_albums()
        {
            var artist = GivenArtist();

            GivenAlbumWithTracks(artist, _released, trackCount: 2, fileCount: 2);
            GivenAlbumWithTracks(artist, _released, trackCount: 2, fileCount: 1);
            GivenAlbumWithTracks(artist, _released, trackCount: 1, monitored: false);
            GivenAlbum(artist, _released);

            var stats = Subject.GetLibraryStatistics();

            stats.AlbumCount.Should().Be(4);
            stats.MonitoredAlbumCount.Should().Be(3);
            stats.CompletedAlbumCount.Should().Be(1);
            stats.MissingAlbumCount.Should().Be(1);
        }

        [Test]
        public void should_count_missing_albums_the_way_wanted_missing_does()
        {
            var artist = GivenArtist();
            var unmonitoredArtist = GivenArtist(monitored: false);

            GivenAlbumWithTracks(artist, _released, trackCount: 3, fileCount: 2);
            GivenAlbumWithTracks(artist, _released, trackCount: 2);
            GivenAlbumWithTracks(artist, _released, trackCount: 2, monitored: false);
            GivenAlbumWithTracks(artist, _unreleased, trackCount: 2);
            GivenAlbumWithTracks(artist, null, trackCount: 2);
            GivenAlbumWithTracks(unmonitoredArtist, _released, trackCount: 2);

            var stats = Subject.GetLibraryStatistics();

            stats.MissingAlbumCount.Should().Be(2);
            stats.MissingTrackCount.Should().Be(3);
        }

        [Test]
        public void should_agree_with_wanted_missing_and_artist_statistics()
        {
            var artist = GivenArtist();
            var unmonitoredArtist = GivenArtist(monitored: false);

            GivenAlbumWithTracks(artist, _released, trackCount: 2, fileCount: 2);
            GivenAlbumWithTracks(artist, _released, trackCount: 3, fileCount: 1);
            GivenAlbumWithTracks(artist, _released, trackCount: 2, monitored: false);
            GivenAlbumWithTracks(artist, _unreleased, trackCount: 2, fileCount: 1);
            GivenAlbumWithTracks(artist, null, trackCount: 1);
            GivenAlbumWithTracks(unmonitoredArtist, _released, trackCount: 2);
            GivenAlbumWithTracks(unmonitoredArtist, _released, trackCount: 1, fileCount: 1);

            var wanted = new PagingSpec<Album> { Page = 1, PageSize = 100 };
            wanted.FilterExpressions.Add(a => a.Monitored == true && a.Artist.Value.Monitored == true);
            var albumStatistics = Mocker.Resolve<ArtistStatisticsRepository>().ArtistStatistics();

            var stats = Subject.GetLibraryStatistics();

            stats.MissingAlbumCount.Should().Be(1);
            stats.MissingAlbumCount.Should().Be(Mocker.Resolve<AlbumRepository>().AlbumsWithoutFiles(wanted).TotalRecords);
            stats.CompletedAlbumCount.Should().Be(3);
            stats.CompletedAlbumCount.Should().Be(albumStatistics.Count(a => a.TrackCount > 0 && a.TrackCount == a.TrackFileCount));
        }

        [Test]
        public void should_count_tracks()
        {
            var artist = GivenArtist();

            GivenAlbumWithTracks(artist, _released, trackCount: 3, fileCount: 1);
            GivenAlbumWithTracks(artist, _released, trackCount: 1, monitored: false);
            GivenAlbumWithTracks(artist, _unreleased, trackCount: 1);
            GivenAlbumWithTracks(artist, null, trackCount: 1);

            var stats = Subject.GetLibraryStatistics();

            stats.TotalTrackCount.Should().Be(6);
            stats.MonitoredTrackCount.Should().Be(5);
            stats.DownloadedTrackCount.Should().Be(1);
            stats.MissingTrackCount.Should().Be(2);
            stats.UnreleasedTrackCount.Should().Be(2);
        }

        [Test]
        public void should_only_count_tracks_on_the_monitored_release()
        {
            var artist = GivenArtist();
            var album = GivenAlbum(artist, _released);
            var monitoredRelease = GivenRelease(album);
            var otherRelease = GivenRelease(album, monitored: false);

            GivenTrack(monitoredRelease, artist);
            GivenTrack(otherRelease, artist);
            GivenTrack(otherRelease, artist);

            var stats = Subject.GetLibraryStatistics();

            stats.TotalTrackCount.Should().Be(1);
            stats.MissingTrackCount.Should().Be(1);
        }

        [Test]
        public void should_not_count_unreleased_track_with_file_as_unreleased()
        {
            GivenAlbumWithTracks(GivenArtist(), _unreleased, trackCount: 1, fileCount: 1);

            var stats = Subject.GetLibraryStatistics();

            stats.DownloadedTrackCount.Should().Be(1);
            stats.UnreleasedTrackCount.Should().Be(0);
        }

        [Test]
        public void should_count_track_whose_file_row_is_gone_as_missing()
        {
            var artist = GivenArtist();
            var release = GivenRelease(GivenAlbum(artist, _released));

            GivenTrack(release, artist, trackFileId: 999);

            var stats = Subject.GetLibraryStatistics();

            stats.DownloadedTrackCount.Should().Be(0);
            stats.MissingTrackCount.Should().Be(1);
            stats.MissingAlbumCount.Should().Be(1);
        }

        [Test]
        public void should_count_album_dated_year_one_as_released()
        {
            GivenAlbumWithTracks(GivenArtist(), new DateTime(1, 1, 1, 0, 0, 0, DateTimeKind.Utc), trackCount: 1);

            var stats = Subject.GetLibraryStatistics();

            stats.MissingTrackCount.Should().Be(1);
            stats.UnreleasedTrackCount.Should().Be(0);
        }

        [Test]
        public void should_sum_track_file_count_and_size_on_disk()
        {
            var album = GivenAlbum(GivenArtist(), _released);

            GivenTrackFile(album.Id, 100);
            GivenTrackFile(album.Id, 200);

            var stats = Subject.GetLibraryStatistics();

            stats.TrackFileCount.Should().Be(2);
            stats.SizeOnDisk.Should().Be(300);
        }

        [Test]
        public void should_count_a_file_shared_by_two_tracks_once()
        {
            var artist = GivenArtist();
            var album = GivenAlbum(artist, _released);
            var release = GivenRelease(album);
            var file = GivenTrackFile(album.Id, 500);

            GivenTrack(release, artist, file.Id);
            GivenTrack(release, artist, file.Id);

            var stats = Subject.GetLibraryStatistics();

            stats.DownloadedTrackCount.Should().Be(2);
            stats.TrackFileCount.Should().Be(1);
            stats.SizeOnDisk.Should().Be(500);
        }

        [Test]
        public void should_not_count_unmapped_track_files()
        {
            GivenAlbum(GivenArtist(), _released);
            GivenTrackFile(0, 700);

            var stats = Subject.GetLibraryStatistics();

            stats.TrackFileCount.Should().Be(0);
            stats.SizeOnDisk.Should().Be(0);
        }

        [Test]
        public void should_filter_artist_counts_by_root_folder()
        {
            GivenArtist(path: "/music/Artist 1".AsOsAgnostic());
            GivenArtist(path: "/music2/Artist 2".AsOsAgnostic());
            GivenArtist(path: "/films/Artist 3".AsOsAgnostic());

            Subject.GetLibraryStatistics().ArtistCount.Should().Be(3);
            Subject.GetLibraryStatistics(new StatisticsFilter { RootFolderPaths = new List<string> { "/music".AsOsAgnostic() } }).ArtistCount.Should().Be(1);
            Subject.GetLibraryStatistics(new StatisticsFilter { RootFolderPaths = new List<string> { "/music/".AsOsAgnostic() } }).ArtistCount.Should().Be(1);
            Subject.GetLibraryStatistics(new StatisticsFilter { RootFolderPaths = new List<string> { "/audiobooks".AsOsAgnostic() } }).ArtistCount.Should().Be(0);
        }

        [Test]
        public void should_filter_album_track_and_file_counts_by_root_folder()
        {
            var artist1 = GivenArtist(path: "/music/Artist 1".AsOsAgnostic());
            var artist2 = GivenArtist(path: "/music2/Artist 2".AsOsAgnostic());

            GivenAlbumWithTracks(artist1, _released, trackCount: 2, fileCount: 1);
            GivenAlbumWithTracks(artist2, _released, trackCount: 3, fileCount: 3);

            var stats = Subject.GetLibraryStatistics(new StatisticsFilter { RootFolderPaths = new List<string> { "/music".AsOsAgnostic() } });

            stats.ArtistCount.Should().Be(1);
            stats.CompletedArtistCount.Should().Be(0);
            stats.AlbumCount.Should().Be(1);
            stats.CompletedAlbumCount.Should().Be(0);
            stats.MissingAlbumCount.Should().Be(1);
            stats.TotalTrackCount.Should().Be(2);
            stats.DownloadedTrackCount.Should().Be(1);
            stats.MissingTrackCount.Should().Be(1);
            stats.TrackFileCount.Should().Be(1);
            stats.SizeOnDisk.Should().Be(100);
        }

        [Test]
        public void should_filter_by_root_folder_with_characters_outside_the_bmp()
        {
            GivenArtist(path: "/music/🎵 Mix/Artist 1".AsOsAgnostic());
            GivenArtist(path: "/music/Artist 2".AsOsAgnostic());

            Subject.GetLibraryStatistics(new StatisticsFilter { RootFolderPaths = new List<string> { "/music/🎵 Mix".AsOsAgnostic() } }).ArtistCount.Should().Be(1);
            Subject.GetLibraryStatistics(new StatisticsFilter { RootFolderPaths = new List<string> { "/music/🎵 Mix".AsOsAgnostic() }, RootFolderPathsNot = true }).ArtistCount.Should().Be(1);
        }

        [Test]
        public void should_skip_blank_root_folder_paths()
        {
            GivenArtist(path: "/music/Artist 1".AsOsAgnostic());
            GivenArtist(path: "/music2/Artist 2".AsOsAgnostic());

            Subject.GetLibraryStatistics(new StatisticsFilter { RootFolderPaths = new List<string> { null, "", " " } }).ArtistCount.Should().Be(2);
            Subject.GetLibraryStatistics(new StatisticsFilter { RootFolderPaths = new List<string> { null, "/music".AsOsAgnostic() } }).ArtistCount.Should().Be(1);
        }

        [Test]
        public void should_filter_by_monitored()
        {
            var monitored = GivenArtist(monitored: true);
            var unmonitored = GivenArtist(monitored: false);

            GivenAlbumWithTracks(monitored, _released, trackCount: 1, fileCount: 1);
            GivenAlbumWithTracks(unmonitored, _released, trackCount: 2, fileCount: 2);

            var monitoredStats = Subject.GetLibraryStatistics(new StatisticsFilter { Monitored = true });
            var unmonitoredStats = Subject.GetLibraryStatistics(new StatisticsFilter { Monitored = false });

            monitoredStats.ArtistCount.Should().Be(1);
            monitoredStats.TrackFileCount.Should().Be(1);
            unmonitoredStats.ArtistCount.Should().Be(1);
            unmonitoredStats.TrackFileCount.Should().Be(2);
        }

        [Test]
        public void should_filter_by_quality_profile()
        {
            var artist = GivenArtist(qualityProfileId: 1);
            GivenArtist(qualityProfileId: 2);
            GivenArtist(qualityProfileId: 3);

            GivenAlbumWithTracks(artist, _released, trackCount: 1, fileCount: 1);

            var stats = Subject.GetLibraryStatistics(new StatisticsFilter { QualityProfileIds = new List<int> { 1 } });

            stats.ArtistCount.Should().Be(1);
            stats.TrackFileCount.Should().Be(1);
            Subject.GetLibraryStatistics(new StatisticsFilter { QualityProfileIds = new List<int> { 2, 3 } }).ArtistCount.Should().Be(2);
            Subject.GetLibraryStatistics(new StatisticsFilter { QualityProfileIds = new List<int> { 1 }, QualityProfileIdsNot = true }).ArtistCount.Should().Be(2);
        }

        [Test]
        public void should_filter_by_metadata_profile()
        {
            var artist = GivenArtist(metadataProfileId: 1);
            GivenArtist(metadataProfileId: 2);
            GivenArtist(metadataProfileId: 3);

            GivenAlbumWithTracks(artist, _released, trackCount: 1, fileCount: 1);

            var stats = Subject.GetLibraryStatistics(new StatisticsFilter { MetadataProfileIds = new List<int> { 1 } });

            stats.ArtistCount.Should().Be(1);
            stats.TrackFileCount.Should().Be(1);
            Subject.GetLibraryStatistics(new StatisticsFilter { MetadataProfileIds = new List<int> { 2, 3 } }).ArtistCount.Should().Be(2);
            Subject.GetLibraryStatistics(new StatisticsFilter { MetadataProfileIds = new List<int> { 1 }, MetadataProfileIdsNot = true }).ArtistCount.Should().Be(2);
        }

        [Test]
        public void should_filter_by_tag()
        {
            var tag = GivenTag("lossless");
            var otherTag = GivenTag("kids");

            var tagged = GivenArtist(tags: new HashSet<int> { tag.Id });
            var otherTagged = GivenArtist(tags: new HashSet<int> { otherTag.Id });
            GivenArtist();

            GivenAlbumWithTracks(tagged, _released, trackCount: 1, fileCount: 1);
            GivenAlbumWithTracks(otherTagged, _released, trackCount: 2, fileCount: 2);

            var stats = Subject.GetLibraryStatistics(new StatisticsFilter { TagIds = new List<int> { tag.Id } });

            stats.ArtistCount.Should().Be(1);
            stats.TrackFileCount.Should().Be(1);
            stats.SizeOnDisk.Should().Be(100);
        }

        [Test]
        public void should_filter_by_multiple_root_folders()
        {
            GivenArtist(path: "/music/Artist 1".AsOsAgnostic());
            GivenArtist(path: "/films/Artist 2".AsOsAgnostic());
            GivenArtist(path: "/music2/Artist 3".AsOsAgnostic());

            var stats = Subject.GetLibraryStatistics(new StatisticsFilter
            {
                RootFolderPaths = new List<string> { "/music".AsOsAgnostic(), "/films".AsOsAgnostic() }
            });

            stats.ArtistCount.Should().Be(2);
        }

        [Test]
        public void should_filter_by_multiple_tags()
        {
            var tag = GivenTag("lossless");
            var otherTag = GivenTag("kids");

            GivenArtist(tags: new HashSet<int> { tag.Id });
            GivenArtist(tags: new HashSet<int> { otherTag.Id });
            GivenArtist();

            Subject.GetLibraryStatistics(new StatisticsFilter { TagIds = new List<int> { tag.Id, otherTag.Id } }).ArtistCount.Should().Be(2);
        }

        [Test]
        public void should_exclude_root_folders()
        {
            GivenArtist(path: "/music/Artist 1".AsOsAgnostic());
            GivenArtist(path: "/music2/Artist 2".AsOsAgnostic());
            GivenArtist(path: "/films/Artist 3".AsOsAgnostic());

            var stats = Subject.GetLibraryStatistics(new StatisticsFilter
            {
                RootFolderPaths = new List<string> { "/music".AsOsAgnostic() },
                RootFolderPathsNot = true
            });

            stats.ArtistCount.Should().Be(2);
        }

        [Test]
        public void should_exclude_tags()
        {
            var tag = GivenTag("lossless");
            var otherTag = GivenTag("kids");

            GivenArtist(tags: new HashSet<int> { tag.Id });
            GivenArtist(tags: new HashSet<int> { tag.Id, otherTag.Id });
            GivenArtist(tags: new HashSet<int> { otherTag.Id });
            GivenArtist();
            GivenArtist();

            Subject.GetLibraryStatistics(new StatisticsFilter { TagIds = new List<int> { tag.Id }, TagIdsNot = true }).ArtistCount.Should().Be(3);
        }

        [Test]
        public void should_combine_filters()
        {
            var tag = GivenTag("lossless");

            var taggedMonitored = GivenArtist(monitored: true, tags: new HashSet<int> { tag.Id });
            var taggedUnmonitored = GivenArtist(monitored: false, tags: new HashSet<int> { tag.Id });
            GivenArtist(monitored: true);

            GivenAlbumWithTracks(taggedMonitored, _released, trackCount: 1, fileCount: 1);
            GivenAlbumWithTracks(taggedUnmonitored, _released, trackCount: 2, fileCount: 2);

            var stats = Subject.GetLibraryStatistics(new StatisticsFilter { TagIds = new List<int> { tag.Id }, Monitored = true });

            stats.ArtistCount.Should().Be(1);
            stats.TrackFileCount.Should().Be(1);
            stats.SizeOnDisk.Should().Be(100);
        }
    }
}
