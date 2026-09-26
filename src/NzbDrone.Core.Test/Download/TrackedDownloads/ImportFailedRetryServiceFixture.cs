using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Music;
using NzbDrone.Core.Music.Events;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download.TrackedDownloads
{
    [TestFixture]
    public class ImportFailedRetryServiceFixture : CoreTest<ImportFailedRetryService>
    {
        private Album _refreshedAlbum;
        private List<TrackedDownload> _trackedDownloads;

        [SetUp]
        public void Setup()
        {
            _refreshedAlbum = new Album { Id = 4, ArtistMetadataId = 7 };
            _trackedDownloads = new List<TrackedDownload>();

            Mocker.GetMock<ITrackedDownloadService>()
                  .Setup(s => s.GetTrackedDownloads())
                  .Returns(_trackedDownloads);

            Mocker.GetMock<IAlbumService>()
                  .Setup(s => s.GetAlbums(It.IsAny<IEnumerable<int>>()))
                  .Returns(new List<Album> { _refreshedAlbum });
        }

        private TrackedDownload GivenTrackedDownload(TrackedDownloadState state, int artistMetadataId = 7)
        {
            var trackedDownload = new TrackedDownload
            {
                State = state,
                DownloadItem = new DownloadClientItem { Title = "Artist - Album" },
                RemoteAlbum = new RemoteAlbum
                {
                    Artist = new Artist { ArtistMetadataId = artistMetadataId },
                    Albums = new List<Album> { new Album { Id = 4, ArtistMetadataId = artistMetadataId } }
                }
            };

            _trackedDownloads.Add(trackedDownload);

            return trackedDownload;
        }

        [Test]
        public void should_retry_failed_import_for_the_refreshed_artist()
        {
            var trackedDownload = GivenTrackedDownload(TrackedDownloadState.ImportFailed);

            Subject.Handle(new AlbumUpdatedEvent(_refreshedAlbum));

            trackedDownload.State.Should().Be(TrackedDownloadState.ImportBlocked);
        }

        [Test]
        public void should_swap_in_the_refreshed_albums()
        {
            var trackedDownload = GivenTrackedDownload(TrackedDownloadState.ImportFailed);

            Subject.Handle(new AlbumUpdatedEvent(_refreshedAlbum));

            trackedDownload.RemoteAlbum.Albums.Should().ContainSingle().Which.Should().BeSameAs(_refreshedAlbum);
        }

        [Test]
        public void should_not_retry_failed_import_for_another_artist()
        {
            var trackedDownload = GivenTrackedDownload(TrackedDownloadState.ImportFailed, artistMetadataId: 8);

            Subject.Handle(new AlbumUpdatedEvent(_refreshedAlbum));

            trackedDownload.State.Should().Be(TrackedDownloadState.ImportFailed);
            Mocker.GetMock<IAlbumService>().Verify(s => s.GetAlbums(It.IsAny<IEnumerable<int>>()), Times.Never());
        }

        [TestCase(TrackedDownloadState.Downloading)]
        [TestCase(TrackedDownloadState.ImportPending)]
        [TestCase(TrackedDownloadState.Imported)]
        [TestCase(TrackedDownloadState.DownloadFailed)]
        [TestCase(TrackedDownloadState.Ignored)]
        public void should_leave_other_states_alone(TrackedDownloadState state)
        {
            var trackedDownload = GivenTrackedDownload(state);

            Subject.Handle(new AlbumUpdatedEvent(_refreshedAlbum));

            trackedDownload.State.Should().Be(state);
        }

        [Test]
        public void should_skip_a_download_that_never_mapped_to_an_artist()
        {
            var trackedDownload = GivenTrackedDownload(TrackedDownloadState.ImportFailed);
            trackedDownload.RemoteAlbum = null;

            Subject.Handle(new AlbumUpdatedEvent(_refreshedAlbum));

            trackedDownload.State.Should().Be(TrackedDownloadState.ImportFailed);
        }
    }
}
