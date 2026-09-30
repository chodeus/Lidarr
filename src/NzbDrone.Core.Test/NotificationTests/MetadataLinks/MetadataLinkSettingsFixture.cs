using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Notifications.Gotify;
using NzbDrone.Core.Notifications.Telegram;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.NotificationTests.MetadataLinks
{
    [TestFixture]
    public class MetadataLinkSettingsFixture : CoreTest
    {
        [Test]
        public void should_reject_an_unknown_metadata_link()
        {
            var settings = new TelegramSettings { BotToken = "token", ChatId = "chat", MetadataLinks = new[] { 99 } };

            settings.Validate().Errors.Should().Contain(e => e.PropertyName == "MetadataLinks");
        }

        [Test]
        public void should_require_the_preferred_link_to_be_selected()
        {
            var settings = new GotifySettings
            {
                Server = "http://gotify.test",
                AppToken = "token",
                MetadataLinks = new[] { (int)MetadataLinkType.MusicBrainzAlbum },
                PreferredMetadataLink = (int)MetadataLinkType.MusicBrainzArtist
            };

            settings.Validate().Errors.Should().Contain(e => e.PropertyName == "PreferredMetadataLink");
        }

        [Test]
        public void should_accept_a_preferred_link_without_links_selected()
        {
            var settings = new GotifySettings { Server = "http://gotify.test", AppToken = "token" };

            settings.Validate().Errors.Should().NotContain(e => e.PropertyName == "PreferredMetadataLink");
        }
    }
}
