using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download
{
    [TestFixture]
    public class IgnoredDownloadServiceFixture : CoreTest<IgnoredDownloadService>
    {
        private TrackedDownload _trackedDownload;

        [SetUp]
        public void Setup()
        {
            _trackedDownload = new TrackedDownload
            {
                DownloadItem = new DownloadClientItem { Title = "Artist Name - Album Title", DownloadId = "download-id" },
                RemoteAlbum = new RemoteAlbum
                {
                    Artist = new Artist { Id = 5 },
                    Albums = new List<Album> { new Album { Id = 7 } },
                    ParsedAlbumInfo = new ParsedAlbumInfo { Quality = new QualityModel(Quality.FLAC) }
                }
            };
        }

        [Test]
        public void should_ignore_a_download_mapped_to_an_artist_and_album()
        {
            Subject.IgnoreDownload(_trackedDownload).Should().BeTrue();

            Mocker.GetMock<IEventAggregator>()
                  .Verify(v => v.PublishEvent(It.Is<DownloadIgnoredEvent>(e => e.ArtistId == 5 && e.AlbumIds.Contains(7))), Times.Once());
        }

        [Test]
        public void should_not_ignore_a_download_that_never_mapped_to_an_artist()
        {
            _trackedDownload.RemoteAlbum = null;

            Subject.IgnoreDownload(_trackedDownload).Should().BeFalse();

            Mocker.GetMock<IEventAggregator>()
                  .Verify(v => v.PublishEvent(It.IsAny<DownloadIgnoredEvent>()), Times.Never());
        }

        [Test]
        public void should_not_ignore_a_download_without_albums()
        {
            _trackedDownload.RemoteAlbum.Albums = null;

            Subject.IgnoreDownload(_trackedDownload).Should().BeFalse();

            Mocker.GetMock<IEventAggregator>()
                  .Verify(v => v.PublishEvent(It.IsAny<DownloadIgnoredEvent>()), Times.Never());
        }
    }
}
