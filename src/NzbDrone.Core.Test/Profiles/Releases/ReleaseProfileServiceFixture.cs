using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Profiles.Releases;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Profiles.Releases
{
    [TestFixture]
    public class ReleaseProfileServiceFixture : CoreTest<ReleaseProfileService>
    {
        private const int ProvidedTag = 1;
        private const int ProvidedTagToExclude = 2;
        private const int NotUsedTag = 3;

        private List<ReleaseProfile> _releaseProfiles;
        private ReleaseProfile _defaultReleaseProfile;
        private ReleaseProfile _includedReleaseProfile;
        private ReleaseProfile _excludedReleaseProfile;
        private ReleaseProfile _includedAndExcludedReleaseProfile;

        [SetUp]
        public void Setup()
        {
            _releaseProfiles = Builder<ReleaseProfile>.CreateListOfSize(5)
                .TheFirst(1)
                .With(r => r.Required = new List<string> { "required_one" })
                .TheNext(1)
                .With(r => r.Required = new List<string> { "required_two" })
                .With(r => r.Tags = new HashSet<int> { ProvidedTag })
                .TheNext(1)
                .With(r => r.Required = new List<string> { "required_three" })
                .With(r => r.ExcludedTags = new HashSet<int> { ProvidedTagToExclude })
                .TheNext(1)
                .With(r => r.Required = new List<string> { "required_four" })
                .With(r => r.Tags = new HashSet<int> { ProvidedTag })
                .With(r => r.ExcludedTags = new HashSet<int> { ProvidedTagToExclude })
                .TheNext(1)
                .With(r => r.Required = new List<string> { "required_five" })
                .With(r => r.Tags = new HashSet<int> { NotUsedTag })
                .Build()
                .ToList();

            _defaultReleaseProfile = _releaseProfiles[0];
            _includedReleaseProfile = _releaseProfiles[1];
            _excludedReleaseProfile = _releaseProfiles[2];
            _includedAndExcludedReleaseProfile = _releaseProfiles[3];

            Mocker.GetMock<IRestrictionRepository>()
                  .Setup(s => s.All())
                  .Returns(_releaseProfiles);
        }

        [Test]
        public void all_for_tags_should_return_release_profiles_without_tags_by_default()
        {
            Subject.AllForTags(new HashSet<int>()).Should().Equal(_defaultReleaseProfile, _excludedReleaseProfile);
        }

        [Test]
        public void all_for_tags_should_return_release_profiles_with_provided_tag_or_without_tags()
        {
            Subject.AllForTags(new HashSet<int> { ProvidedTag }).Should().Equal(_defaultReleaseProfile, _includedReleaseProfile, _excludedReleaseProfile, _includedAndExcludedReleaseProfile);
        }

        [Test]
        public void all_for_tags_should_not_return_release_profiles_with_provided_tag_excluded()
        {
            var releaseProfiles = Subject.AllForTags(new HashSet<int> { ProvidedTagToExclude });

            releaseProfiles.Should().NotContain(_excludedReleaseProfile);
            releaseProfiles.Should().NotContain(_includedAndExcludedReleaseProfile);
        }

        [Test]
        public void all_for_tags_should_not_return_release_profiles_with_a_provided_tag_both_included_and_excluded()
        {
            Subject.AllForTags(new HashSet<int> { ProvidedTag, ProvidedTagToExclude }).Should().Equal(_defaultReleaseProfile, _includedReleaseProfile);
        }

        [Test]
        public void all_for_tag_should_return_release_profiles_with_provided_tag()
        {
            Subject.AllForTag(ProvidedTag).Should().Equal(_includedReleaseProfile, _includedAndExcludedReleaseProfile);
        }

        [Test]
        public void all_excluded_for_tag_should_return_release_profiles_excluding_the_tag()
        {
            Subject.AllExcludedForTag(ProvidedTagToExclude).Should().Equal(_excludedReleaseProfile, _includedAndExcludedReleaseProfile);
        }

        [TestCase(5, true)]
        [TestCase(7, true)]
        [TestCase(9, false)]
        public void enabled_for_tags_should_apply_a_profile_to_its_indexers_only(int indexerId, bool applies)
        {
            var releaseProfile = new ReleaseProfile { Id = 1, Required = new List<string> { "flac" }, IndexerIds = new List<int> { 5, 7 } };
            Mocker.GetMock<IRestrictionRepository>().Setup(s => s.All()).Returns(new List<ReleaseProfile> { releaseProfile });

            Subject.EnabledForTags(new HashSet<int>(), indexerId).Contains(releaseProfile).Should().Be(applies);
        }

        [Test]
        public void enabled_for_tags_should_apply_a_profile_without_indexers_to_every_indexer()
        {
            var releaseProfile = new ReleaseProfile { Id = 1, Required = new List<string> { "flac" } };
            Mocker.GetMock<IRestrictionRepository>().Setup(s => s.All()).Returns(new List<ReleaseProfile> { releaseProfile });

            Subject.EnabledForTags(new HashSet<int>(), 9).Should().Equal(releaseProfile);
        }

        [Test]
        public void should_read_and_set_develops_single_indexer_through_the_indexer_list()
        {
            var releaseProfile = new ReleaseProfile { IndexerIds = new List<int> { 5, 7 } };

            releaseProfile.IndexerId.Should().Be(5);

            releaseProfile.IndexerId = 9;
            releaseProfile.IndexerIds.Should().Equal(9);

            releaseProfile.IndexerId = 0;
            releaseProfile.IndexerIds.Should().BeEmpty();
        }

        [Test]
        public void should_give_the_member_added_to_the_develop_interface_a_default()
        {
            typeof(IReleaseProfileService).GetMethod(nameof(IReleaseProfileService.AllExcludedForTag)).IsAbstract.Should().BeFalse();
        }
    }
}
