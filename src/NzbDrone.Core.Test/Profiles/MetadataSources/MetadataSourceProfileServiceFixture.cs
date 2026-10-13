using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Core.MetadataSource.Sources;
using NzbDrone.Core.Profiles.MetadataSources;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.ThingiProvider.Events;

namespace NzbDrone.Core.Test.Profiles.MetadataSources
{
    [TestFixture]
    public class MetadataSourceProfileServiceFixture : DbTest<MetadataSourceProfileService, MetadataSourceProfile>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<ICacheManager>(Mocker.Resolve<CacheManager>());
            Mocker.SetConstant<IMetadataSourceProfileRepository>(Mocker.Resolve<MetadataSourceProfileRepository>());
        }

        private MetadataSourceProfile GivenProfile(string name, params int[] tags)
        {
            return Subject.Add(new MetadataSourceProfile
            {
                Name = name,
                Tags = new HashSet<int>(tags),
                Items = new List<MetadataSourceProfileItem> { new() { MetadataSourceId = 1, Role = MetadataSourceRole.Primary } }
            });
        }

        [Test]
        public void should_seed_a_default_profile_that_uses_lidarrs_own_server()
        {
            var source = Mocker.Resolve<MetadataSourceRepository>().All().Single();

            source.Implementation.Should().Be(nameof(NzbDrone.Core.MetadataSource.Sources.Lidarr.LidarrMetadataSource));
            source.Enable.Should().BeTrue();
            Subject.GetDefaultProfile().Items.Should().ContainSingle(i => i.MetadataSourceId == source.Id && i.Role == MetadataSourceRole.Primary);
            Subject.GetDefaultProfile().Tags.Should().BeEmpty();
        }

        [Test]
        public void best_for_tags_picks_the_first_profile_sharing_a_tag()
        {
            var first = GivenProfile("First", 5);
            GivenProfile("Second", 5, 6);

            Subject.BestForTags(new HashSet<int> { 5, 6 }).Id.Should().Be(first.Id);
        }

        [Test]
        public void best_for_tags_falls_back_to_the_default_profile()
        {
            GivenProfile("Tagged", 5);

            Subject.BestForTags(new HashSet<int> { 7 }).Id.Should().Be(MetadataSourceProfileService.DefaultProfileId);
        }

        [Test]
        public void knows_whether_any_profile_has_tags()
        {
            Subject.HasTaggedProfiles().Should().BeFalse();

            GivenProfile("Tagged", 5);

            Subject.HasTaggedProfiles().Should().BeTrue();
        }

        [Test]
        public void deleting_a_source_removes_it_from_every_profile()
        {
            var tagged = GivenProfile("Tagged", 5);

            Subject.Handle(new ProviderDeletedEvent<IMetadataSource>(1));

            Subject.Get(tagged.Id).Items.Should().BeEmpty();
            Subject.GetDefaultProfile().Items.Should().BeEmpty();
        }
    }
}
