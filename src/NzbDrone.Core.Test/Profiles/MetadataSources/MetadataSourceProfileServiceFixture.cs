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

        private IEnumerable<string> ProfilesInOrder()
        {
            return Subject.All().OrderBy(p => p.Order).Select(p => p.Name);
        }

        private IEnumerable<int> OrdersInOrder()
        {
            return Subject.All().OrderBy(p => p.Order).Select(p => p.Order);
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
        public void all_for_tag_returns_the_profiles_using_the_tag()
        {
            var first = GivenProfile("First", 5);
            GivenProfile("Second", 6);
            var third = GivenProfile("Third", 5, 6);

            Subject.AllForTag(5).Select(p => p.Id).Should().BeEquivalentTo(new[] { first.Id, third.Id });
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
        public void knows_whether_any_profile_uses_a_supplementary_source()
        {
            Subject.HasSupplementarySources().Should().BeFalse();

            Subject.Add(new MetadataSourceProfile
            {
                Name = "Supplemented",
                Tags = new HashSet<int> { 5 },
                Items = new List<MetadataSourceProfileItem> { new() { MetadataSourceId = 1, Role = MetadataSourceRole.Supplementary } }
            });

            Subject.HasSupplementarySources().Should().BeTrue();
        }

        [Test]
        public void reorder_moves_a_profile_down()
        {
            var a = GivenProfile("A", 1);
            var b = GivenProfile("B", 2);
            GivenProfile("C", 3);

            Subject.Reorder(a.Id, b.Id);

            ProfilesInOrder().Should().Equal("B", "A", "C", "Default");
            OrdersInOrder().Should().Equal(1, 2, 3, int.MaxValue);
        }

        [Test]
        public void reorder_moves_a_profile_up()
        {
            var a = GivenProfile("A", 1);
            GivenProfile("B", 2);
            var c = GivenProfile("C", 3);

            Subject.Reorder(c.Id, a.Id);

            ProfilesInOrder().Should().Equal("A", "C", "B", "Default");
            OrdersInOrder().Should().Equal(1, 2, 3, int.MaxValue);
        }

        [Test]
        public void reorder_without_an_after_id_moves_a_profile_to_the_top()
        {
            GivenProfile("A", 1);
            GivenProfile("B", 2);
            var c = GivenProfile("C", 3);

            Subject.Reorder(c.Id, null);

            ProfilesInOrder().Should().Equal("C", "A", "B", "Default");
            OrdersInOrder().Should().Equal(1, 2, 3, int.MaxValue);
        }

        [Test]
        public void reorder_after_the_default_moves_a_profile_last()
        {
            var a = GivenProfile("A", 1);
            GivenProfile("B", 2);
            GivenProfile("C", 3);

            Subject.Reorder(a.Id, MetadataSourceProfileService.DefaultProfileId);

            ProfilesInOrder().Should().Equal("B", "C", "A", "Default");
            OrdersInOrder().Should().Equal(1, 2, 3, int.MaxValue);
        }

        [Test]
        public void reorder_never_moves_the_default()
        {
            GivenProfile("A", 1);
            GivenProfile("B", 2);

            Subject.Reorder(MetadataSourceProfileService.DefaultProfileId, null);

            ProfilesInOrder().Should().Equal("A", "B", "Default");
            OrdersInOrder().Should().Equal(1, 2, int.MaxValue);
        }

        [Test]
        public void reorder_onto_itself_changes_nothing()
        {
            var a = GivenProfile("A", 1);
            GivenProfile("B", 2);
            GivenProfile("C", 3);

            Subject.Reorder(a.Id, a.Id);

            ProfilesInOrder().Should().Equal("A", "B", "C", "Default");
            OrdersInOrder().Should().Equal(1, 2, 3, int.MaxValue);
        }

        [Test]
        public void deleting_a_profile_renumbers_the_rest()
        {
            GivenProfile("A", 1);
            var b = GivenProfile("B", 2);
            GivenProfile("C", 3);

            Subject.Delete(b.Id);

            ProfilesInOrder().Should().Equal("A", "C", "Default");
            OrdersInOrder().Should().Equal(1, 2, int.MaxValue);
        }

        [Test]
        public void update_keeps_the_stored_order()
        {
            var a = GivenProfile("A", 1);
            GivenProfile("B", 2);

            a.Order = 2;
            a.Name = "Renamed";
            Subject.Update(a);

            ProfilesInOrder().Should().Equal("Renamed", "B", "Default");
            OrdersInOrder().Should().Equal(1, 2, int.MaxValue);
        }

        [Test]
        public void update_keeps_the_default_profile_last()
        {
            var profile = Subject.GetDefaultProfile();
            profile.Order = 0;

            Subject.Update(profile);

            Subject.Get(MetadataSourceProfileService.DefaultProfileId).Order.Should().Be(int.MaxValue);
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
