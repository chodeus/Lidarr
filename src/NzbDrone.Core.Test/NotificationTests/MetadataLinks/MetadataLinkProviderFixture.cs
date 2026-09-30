using System;
using System.Collections.Generic;
using System.Text;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Music;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Notifications.Gotify;
using NzbDrone.Core.Notifications.Pushcut;
using NzbDrone.Core.Notifications.Telegram;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.NotificationTests.MetadataLinks
{
    [TestFixture]
    public class MetadataLinkProviderFixture : CoreTest
    {
        private List<NotificationMetadataLink> _links;
        private HttpRequest _request;

        [SetUp]
        public void Setup()
        {
            _request = null;

            _links = new List<NotificationMetadataLink>
            {
                new NotificationMetadataLink(MetadataLinkType.MusicBrainzArtist, "MusicBrainz Artist", "https://musicbrainz.org/artist/artist-mbid")
            };

            Mocker.GetMock<IHttpClient>()
                  .Setup(s => s.Post(It.IsAny<HttpRequest>()))
                  .Callback<HttpRequest>(r => _request = r);

            Mocker.GetMock<IHttpClient>()
                  .Setup(s => s.Execute(It.IsAny<HttpRequest>()))
                  .Callback<HttpRequest>(r => _request = r);
        }

        private string Body => Uri.UnescapeDataString(Encoding.UTF8.GetString(_request.ContentData));

        [Test]
        public void telegram_should_add_links_as_anchors()
        {
            Mocker.Resolve<TelegramProxy>().SendNotification("Title", "Message", _links, new TelegramSettings());

            Body.Should().Contain("<a href=\"https://musicbrainz.org/artist/artist-mbid\">MusicBrainz Artist</a>");
        }

        [Test]
        public void pushcut_should_add_links_as_actions()
        {
            Mocker.Resolve<PushcutProxy>().SendNotification("Title", "Message", _links, new PushcutSettings());

            Body.Should().Contain("\"actions\": [").And.Contain("\"url\": \"https://musicbrainz.org/artist/artist-mbid\"");
        }

        [Test]
        public void telegram_develop_overload_should_send_without_links()
        {
            Mocker.Resolve<TelegramProxy>().SendNotification("Title", "Message", new TelegramSettings());

            Body.Should().Contain("Message").And.NotContain("<a href");
        }

        [Test]
        public void pushcut_develop_overload_should_send_without_actions()
        {
            Mocker.Resolve<PushcutProxy>().SendNotification("Title", "Message", new PushcutSettings());

            Body.Should().Contain("\"actions\": []");
        }

        [Test]
        public void gotify_should_add_links_and_click_through_to_the_preferred_one()
        {
            GotifyMessage payload = null;

            Mocker.GetMock<IGotifyProxy>()
                  .Setup(s => s.SendNotification(It.IsAny<GotifyMessage>(), It.IsAny<GotifySettings>()))
                  .Callback<GotifyMessage, GotifySettings>((p, _) => payload = p);

            var gotify = Mocker.Resolve<Gotify>();
            gotify.Definition = new NotificationDefinition
            {
                Settings = new GotifySettings
                {
                    MetadataLinks = new[] { (int)MetadataLinkType.MusicBrainzArtist, (int)MetadataLinkType.MusicBrainzAlbum },
                    PreferredMetadataLink = (int)MetadataLinkType.MusicBrainzAlbum
                }
            };

            gotify.OnGrab(new GrabMessage
            {
                Message = "Grabbed",
                Artist = new Artist { Metadata = new LazyLoaded<ArtistMetadata>(new ArtistMetadata { ForeignArtistId = "artist-mbid" }) },
                RemoteAlbum = new RemoteAlbum { Albums = new List<Album> { new Album { ForeignAlbumId = "album-mbid" } } }
            });

            payload.Message.Should().Contain("(https://musicbrainz.org/artist/artist-mbid)").And.Contain("(https://musicbrainz.org/release-group/album-mbid)");
            payload.Extras.ClientNotification.Click.Url.Should().Be("https://musicbrainz.org/release-group/album-mbid");
            payload.Extras.ClientDisplay.ContentType.Should().Be("text/markdown");
        }
    }
}
