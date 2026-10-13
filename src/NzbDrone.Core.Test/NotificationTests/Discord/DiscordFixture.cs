using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Music;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Notifications.Discord;
using NzbDrone.Core.Notifications.Discord.Payloads;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.NotificationTests.Discord
{
    [TestFixture]
    public class DiscordFixture : CoreTest<Core.Notifications.Discord.Discord>
    {
        private DiscordPayload _payload;

        [SetUp]
        public void Setup()
        {
            Subject.Definition = new NotificationDefinition
            {
                Settings = new DiscordSettings
                {
                    WebHookUrl = "https://discord.example.invalid/webhook",
                    GrabFields = new[] { (int)DiscordGrabFieldType.Links }
                }
            };

            Mocker.GetMock<IDiscordProxy>()
                .Setup(p => p.SendPayload(It.IsAny<DiscordPayload>(), It.IsAny<DiscordSettings>()))
                .Callback<DiscordPayload, DiscordSettings>((payload, settings) => _payload = payload);
        }

        private Embed GivenGrab(string foreignArtistId, List<Links> links)
        {
            Subject.OnGrab(new GrabMessage
            {
                Artist = new Artist { Metadata = new ArtistMetadata { ForeignArtistId = foreignArtistId, Name = "Placeholder Artist", Links = links } },
                RemoteAlbum = new RemoteAlbum { Albums = new List<Album> { new Album { Title = "Placeholder Album" } } }
            });

            return _payload.Embeds[0];
        }

        [Test]
        public void grab_links_a_musicbrainz_artist_to_musicbrainz()
        {
            var embed = GivenGrab("11111111-1111-1111-1111-111111111111", new List<Links>());

            embed.Url.Should().Be("https://musicbrainz.org/artist/11111111-1111-1111-1111-111111111111");
            embed.Fields.Should().ContainSingle(f => f.Value == "[MusicBrainz](https://musicbrainz.org/artist/11111111-1111-1111-1111-111111111111)");
        }

        [Test]
        public void grab_links_another_sources_artist_to_its_own_page()
        {
            var embed = GivenGrab("other:1", new List<Links> { new Links { Name = "Source", Url = "https://source.example.invalid/artist/1" } });

            embed.Url.Should().Be("https://source.example.invalid/artist/1");
            embed.Fields.Should().ContainSingle(f => f.Value == "[Source](https://source.example.invalid/artist/1)");
        }
    }
}
