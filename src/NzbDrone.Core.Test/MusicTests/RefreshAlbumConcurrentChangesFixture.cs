using System;
using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Music;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MusicTests
{
    // The real AlbumService over the test database; the lookup and the release refresh are mocked
    [TestFixture]
    public class RefreshAlbumConcurrentChangesFixture : DbTest<RefreshAlbumService, Album>
    {
        private ArtistMetadata _metadata;
        private Album _album;
        private AlbumService _albumService;

        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<IAlbumRepository>(Mocker.Resolve<AlbumRepository>());
            _albumService = Mocker.Resolve<AlbumService>();
            Mocker.SetConstant<IAlbumService>(_albumService);

            _metadata = Builder<ArtistMetadata>.CreateNew().With(m => m.Id = 0).With(m => m.ForeignArtistId = "artist-1").Build();
            Db.Insert(_metadata);

            _album = Builder<Album>.CreateNew()
                                   .With(a => a.Id = 0)
                                   .With(a => a.ArtistMetadataId = _metadata.Id)
                                   .With(a => a.ForeignAlbumId = "album-1")
                                   .With(a => a.Title = "Album Title")
                                   .With(a => a.Monitored = true)
                                   .With(a => a.AnyReleaseOk = false)
                                   .With(a => a.ProfileId = 1)
                                   .With(a => a.LastSearchTime = null)
                                   .With(a => a.Images = new List<MediaCover.MediaCover>())
                                   .With(a => a.Links = new List<Links>())
                                   .With(a => a.Genres = new List<string>())
                                   .With(a => a.SecondaryTypes = new List<SecondaryAlbumType>())
                                   .With(a => a.OldForeignAlbumIds = new List<string>())
                                   .With(a => a.MetadataMissingSince = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))
                                   .With(a => a.LastInfoSync = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))
                                   .Build();
            Db.Insert(_album);

            Mocker.GetMock<IArtistService>().Setup(s => s.FindById(It.IsAny<string>())).Returns(new Artist { Id = 1 });
            Mocker.GetMock<IArtistMetadataService>().Setup(s => s.UpsertMany(It.IsAny<List<ArtistMetadata>>())).Returns(true);
            Mocker.GetMock<IReleaseService>().Setup(s => s.GetReleasesForRefresh(It.IsAny<int>(), It.IsAny<List<string>>())).Returns(new List<AlbumRelease>());
            Mocker.GetMock<IMediaFileService>().Setup(s => s.GetFilesByAlbum(It.IsAny<int>())).Returns(new List<TrackFile>());
            Mocker.GetMock<IMediaFileService>().Setup(s => s.GetFilesByRelease(It.IsAny<int>())).Returns(new List<TrackFile>());

            GivenLookup(() => { });
        }

        // What the metadata source returns: every field the refresh copies changed, and one release
        private void GivenLookup(Action duringLookup)
        {
            Mocker.GetMock<IProvideAlbumInfo>()
                  .Setup(s => s.GetAlbumInfo(It.IsAny<string>()))
                  .Returns(() =>
                  {
                      duringLookup();

                      var remote = Builder<Album>.CreateNew()
                                                 .With(a => a.Id = 0)
                                                 .With(a => a.ForeignAlbumId = "album-1")
                                                 .With(a => a.OldForeignAlbumIds = new List<string> { "album-0" })
                                                 .With(a => a.Title = "Album Title Renamed")
                                                 .With(a => a.CleanTitle = "albumtitlerenamed")
                                                 .With(a => a.Overview = "Album Overview")
                                                 .With(a => a.Disambiguation = "Album Disambiguation")
                                                 .With(a => a.ReleaseDate = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc))
                                                 .With(a => a.Images = new List<MediaCover.MediaCover> { new MediaCover.MediaCover { CoverType = MediaCoverTypes.Cover, Url = "https://example.test/cover.jpg" } })
                                                 .With(a => a.Links = new List<Links> { new Links { Name = "Link Name", Url = "https://example.test/album" } })
                                                 .With(a => a.Genres = new List<string> { "Genre Name" })
                                                 .With(a => a.AlbumType = "EP")
                                                 .With(a => a.SecondaryTypes = new List<SecondaryAlbumType> { SecondaryAlbumType.Live })
                                                 .With(a => a.Ratings = new Ratings { Votes = 3, Value = 4.5m })
                                                 .With(a => a.ArtistMetadata = _metadata)
                                                 .With(a => a.AlbumReleases = new List<AlbumRelease>
                                                 {
                                                     Builder<AlbumRelease>.CreateNew().With(r => r.Id = 0).With(r => r.ForeignReleaseId = "release-1").With(r => r.Media = new List<Medium>()).Build()
                                                 })
                                                 .Build();

                      return Tuple.Create("artist-1", remote, new List<ArtistMetadata> { _metadata });
                  });
        }

        [Test]
        public void should_keep_monitoring_profile_and_search_time_saved_during_the_lookup()
        {
            var searched = new DateTime(2026, 10, 20, 8, 0, 0, DateTimeKind.Utc);

            GivenLookup(() =>
            {
                var album = _albumService.GetAlbum(_album.Id);
                album.Monitored = false;
                album.AnyReleaseOk = true;
                album.ProfileId = 2;
                _albumService.UpdateAlbum(album);

                album.LastSearchTime = searched;
                _albumService.UpdateLastSearchTime(new List<Album> { album });
            });

            Subject.RefreshAlbumInfo(_albumService.GetAlbum(_album.Id), null, false);

            var stored = _albumService.GetAlbum(_album.Id);
            new { stored.Monitored, stored.AnyReleaseOk, stored.ProfileId, stored.LastSearchTime, stored.Title }
                .Should().BeEquivalentTo(new { Monitored = false, AnyReleaseOk = true, ProfileId = 2, LastSearchTime = (DateTime?)searched, Title = "Album Title Renamed" });
        }

        // Fails by name for a field the refresh sets but doesn't save, such as one added upstream
        [Test]
        public void should_save_every_field_the_refresh_sets()
        {
            var refreshed = _albumService.GetAlbum(_album.Id);

            Subject.RefreshAlbumInfo(refreshed, null, false);

            RefreshedFields.ShouldMatch(refreshed, _albumService.GetAlbum(_album.Id), "ProfileId", "Monitored", "AnyReleaseOk", "LastSearchTime", "Added", "AddOptions");
        }
    }
}
