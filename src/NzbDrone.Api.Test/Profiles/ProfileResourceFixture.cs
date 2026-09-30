using System.Collections.Generic;
using FluentAssertions;
using Lidarr.Api.V1.Profiles.Delay;
using Lidarr.Api.V1.Profiles.Tagging;
using NUnit.Framework;

namespace NzbDrone.Api.Test.Profiles
{
    [TestFixture]
    public class ProfileResourceFixture
    {
        [Test]
        public void delay_profile_without_tags_should_map_to_no_tags()
        {
            new DelayProfileResource { Items = new List<DelayProfileProtocolItemResource>(), Tags = null }.ToModel().Tags.Should().BeEmpty();
        }

        [Test]
        public void tagging_profile_without_tags_should_map_to_no_tags()
        {
            new TaggingProfileResource { Tags = null }.ToModel().Tags.Should().BeEmpty();
        }
    }
}
