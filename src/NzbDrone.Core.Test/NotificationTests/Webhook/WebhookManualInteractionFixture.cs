using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Notifications.Webhook;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.NotificationTests.Webhook
{
    [TestFixture]
    public class WebhookManualInteractionFixture : CoreTest<Notifications.Webhook.Webhook>
    {
        private WebhookPayload _payload;

        [SetUp]
        public void Setup()
        {
            Subject.Definition = new NotificationDefinition { Settings = new WebhookSettings { Url = "http://webhook.test" } };

            Mocker.GetMock<IWebhookProxy>()
                  .Setup(p => p.SendWebhook(It.IsAny<WebhookPayload>(), It.IsAny<WebhookSettings>()))
                  .Callback<WebhookPayload, WebhookSettings>((payload, settings) => _payload = payload);
        }

        [Test]
        public void should_send_a_payload_for_a_download_that_never_mapped_to_an_artist()
        {
            var message = new ManualInteractionRequiredMessage
            {
                Message = "Artist.Name-Album.Title-GROUP",
                TrackedDownload = new TrackedDownload { DownloadItem = new DownloadClientItem { Title = "Artist.Name-Album.Title-GROUP", TotalSize = 10 } },
                DownloadId = "download-id"
            };

            Subject.OnManualInteractionRequired(message);

            var payload = _payload.Should().BeOfType<WebhookManualInteractionPayload>().Subject;
            payload.EventType.Should().Be(WebhookEventType.ManualInteractionRequired);
            payload.Artist.Should().BeNull();
            payload.Albums.Should().BeNull();
            payload.DownloadInfo.Title.Should().Be("Artist.Name-Album.Title-GROUP");
            payload.DownloadInfo.Quality.Should().BeNull();
            payload.DownloadId.Should().Be("download-id");
        }
    }
}
