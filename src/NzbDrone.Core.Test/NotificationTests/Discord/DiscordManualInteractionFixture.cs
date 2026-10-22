using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Music;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Notifications.Discord;
using NzbDrone.Core.Notifications.Discord.Payloads;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.NotificationTests.Discord
{
    [TestFixture]
    public class DiscordManualInteractionFixture : CoreTest<Notifications.Discord.Discord>
    {
        private DiscordPayload _payload;

        [SetUp]
        public void Setup()
        {
            Subject.Definition = new NotificationDefinition { Settings = new DiscordSettings() };

            Mocker.GetMock<IDiscordProxy>()
                  .Setup(p => p.SendPayload(It.IsAny<DiscordPayload>(), It.IsAny<DiscordSettings>()))
                  .Callback<DiscordPayload, DiscordSettings>((p, _) => _payload = p);
        }

        private void GivenUnmatchedDownload(string title)
        {
            Subject.OnManualInteractionRequired(new ManualInteractionRequiredMessage
            {
                Message = title,
                TrackedDownload = new TrackedDownload { DownloadItem = new DownloadClientItem { Title = title } }
            });
        }

        [Test]
        public void should_use_the_download_title_for_an_unmatched_download()
        {
            GivenUnmatchedDownload("Artist.Name-Album.Title-GROUP");

            _payload.Embeds[0].Title.Should().Be("Artist.Name-Album.Title-GROUP");
        }

        [TestCase(1018)]
        [TestCase(1019)]
        [TestCase(3000)]
        public void should_keep_the_download_field_within_1024_characters(int titleLength)
        {
            GivenUnmatchedDownload(new string('a', titleLength));

            var field = _payload.Embeds[0].Fields.Single(f => f.Name == "Download");

            field.Value.Length.Should().BeLessOrEqualTo(1024);
            field.Value.Should().StartWith("```").And.EndWith("```");
        }

        [TestCase("11111111-1111-1111-1111-111111111111", "https://musicbrainz.org/artist/11111111-1111-1111-1111-111111111111")]
        [TestCase("other:1", null)]
        public void should_link_only_a_musicbrainz_artist(string foreignArtistId, string url)
        {
            Subject.OnManualInteractionRequired(new ManualInteractionRequiredMessage
            {
                Message = "Placeholder.Title",
                Artist = new Artist { Metadata = new ArtistMetadata { ForeignArtistId = foreignArtistId, Name = "Placeholder Artist" } },
                TrackedDownload = new TrackedDownload { DownloadItem = new DownloadClientItem { Title = "Placeholder.Title" } }
            });

            _payload.Embeds[0].Url.Should().Be(url);

            var links = _payload.Embeds[0].Fields.Where(f => f.Name == "Links").Select(f => f.Value);

            if (url == null)
            {
                links.Should().BeEmpty();
            }
            else
            {
                links.Should().ContainSingle().Which.Should().Contain(url);
            }
        }

        [Test]
        public void should_limit_a_long_download_title_to_256_characters()
        {
            GivenUnmatchedDownload(new string('a', 300));

            _payload.Embeds[0].Title.Should().HaveLength(256).And.EndWith("...");
        }
    }
}
