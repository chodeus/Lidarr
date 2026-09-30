using System.Collections.Generic;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Music;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.NotificationTests
{
    [TestFixture]
    public class ManualInteractionRequiredFixture : CoreTest<NotificationService>
    {
        private TrackedDownload _trackedDownload;
        private Mock<INotification> _notification;
        private NotificationDefinition _definition;

        [SetUp]
        public void Setup()
        {
            _trackedDownload = new TrackedDownload
            {
                DownloadItem = new DownloadClientItem { Title = "Artist.Name-Album.Title-GROUP", DownloadId = "download-id" }
            };

            _definition = new NotificationDefinition { Id = 1, Name = "Test", Tags = new HashSet<int>() };

            _notification = new Mock<INotification>();
            _notification.SetupGet(n => n.Definition).Returns(_definition);

            Mocker.GetMock<INotificationFactory>()
                  .Setup(f => f.OnManualInteractionEnabled(It.IsAny<bool>()))
                  .Returns(new List<INotification> { _notification.Object });
        }

        private void GivenMatchedAlbum(QualityModel quality)
        {
            _trackedDownload.RemoteAlbum = new RemoteAlbum
            {
                Artist = new Artist { Name = "Artist Name", Tags = new HashSet<int> { 5 } },
                Albums = new List<Album> { new Album { Title = "Album Title" } },
                ParsedAlbumInfo = new ParsedAlbumInfo { Quality = quality }
            };
        }

        [Test]
        public void should_describe_a_matched_download_by_artist_album_and_quality()
        {
            GivenMatchedAlbum(new QualityModel(Quality.FLAC));

            Subject.Handle(new ManualInteractionRequiredEvent(_trackedDownload));

            _notification.Verify(n => n.OnManualInteractionRequired(It.Is<ManualInteractionRequiredMessage>(m => m.Message == "Artist Name - Album Title - [FLAC]")), Times.Once());
        }

        [Test]
        public void should_fall_back_to_the_download_title_without_a_quality()
        {
            GivenMatchedAlbum(null);

            Subject.Handle(new ManualInteractionRequiredEvent(_trackedDownload));

            _notification.Verify(n => n.OnManualInteractionRequired(It.Is<ManualInteractionRequiredMessage>(m => m.Message == "Artist.Name-Album.Title-GROUP")), Times.Once());
        }

        [Test]
        public void should_send_an_unmatched_download_to_a_notification_without_tags()
        {
            Subject.Handle(new ManualInteractionRequiredEvent(_trackedDownload));

            _notification.Verify(n => n.OnManualInteractionRequired(It.Is<ManualInteractionRequiredMessage>(m => m.Message == "Artist.Name-Album.Title-GROUP" && m.Artist == null)), Times.Once());
        }

        [Test]
        public void should_not_send_an_unmatched_download_to_a_notification_with_tags()
        {
            _definition.Tags.Add(5);

            Subject.Handle(new ManualInteractionRequiredEvent(_trackedDownload));

            _notification.Verify(n => n.OnManualInteractionRequired(It.IsAny<ManualInteractionRequiredMessage>()), Times.Never());
        }

        [Test]
        public void should_send_a_matched_download_to_a_notification_sharing_its_tags()
        {
            GivenMatchedAlbum(new QualityModel(Quality.FLAC));
            _definition.Tags.Add(5);

            Subject.Handle(new ManualInteractionRequiredEvent(_trackedDownload));

            _notification.Verify(n => n.OnManualInteractionRequired(It.IsAny<ManualInteractionRequiredMessage>()), Times.Once());
        }
    }
}
