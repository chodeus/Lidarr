using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MetadataSource.Sources;
using NzbDrone.Core.MetadataSource.Sources.SkyHookServer;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource.Sources
{
    [TestFixture]
    public class MetadataSourceSettingsFixture : CoreTest
    {
        [TestCase("")]
        [TestCase("not a url")]
        public void server_url_must_be_a_url(string url)
        {
            new SkyHookServerMetadataSourceSettings { Url = url }.Validate().IsValid.Should().BeFalse();
        }

        [TestCase("https://metadata.example.invalid/api/v0.4?apikey=placeholder")]
        [TestCase("https://user:placeholder@metadata.example.invalid/api/v0.4")]
        public void server_url_must_not_carry_a_query_or_user_info(string url)
        {
            new SkyHookServerMetadataSourceSettings { Url = url }.Validate().IsValid.Should().BeFalse();
        }

        [Test]
        public void server_url_can_include_a_path()
        {
            new SkyHookServerMetadataSourceSettings { Url = "https://metadata.example.invalid/api/v0.4" }.Validate().IsValid.Should().BeTrue();
        }

        [TestCase("11111111-1111-1111-1111-111111111111", true)]
        [TestCase("11111111111111111111111111111111", false)]
        [TestCase("other:1", false)]
        [TestCase("", false)]
        public void recognises_musicbrainz_ids(string id, bool expected)
        {
            MusicBrainzIds.IsMusicBrainzId(id).Should().Be(expected);
        }
    }
}
