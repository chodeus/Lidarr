using System.Collections.Generic;
using FluentAssertions;
using FluentValidation.Results;
using Lidarr.Api.V1.Profiles.Release;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Profiles.Releases;

namespace NzbDrone.Api.Test.Profiles
{
    [TestFixture]
    public class ReleaseProfileControllerFixture
    {
        private Mock<IIndexerFactory> _indexerFactory;
        private TestController _subject;

        [SetUp]
        public void Setup()
        {
            _indexerFactory = new Mock<IIndexerFactory>();
            _subject = new TestController(new Mock<IReleaseProfileService>().Object, _indexerFactory.Object);
        }

        private static ReleaseProfileResource Resource()
        {
            return new ReleaseProfileResource { Enabled = true, Required = new List<string> { "flac" }, Ignored = new List<string>() };
        }

        [Test]
        public void should_reject_a_tag_in_both_tags_and_excluded_tags()
        {
            var resource = Resource();
            resource.Tags = new HashSet<int> { 1, 2 };
            resource.ExcludedTags = new HashSet<int> { 2 };

            var error = _subject.Validate(resource).Errors.Should().ContainSingle().Subject;

            error.PropertyName.Should().Be("ExcludedTags");
            error.ErrorMessage.Should().Be("A tag cannot be in both 'Tags' and 'Excluded Tags'");
        }

        [Test]
        public void should_reject_excluded_tags_sent_as_null()
        {
            var resource = Resource();
            resource.ExcludedTags = null;

            var error = _subject.Validate(resource).Errors.Should().ContainSingle().Subject;

            error.PropertyName.Should().Be("ExcludedTags");
        }

        [Test]
        public void should_accept_tags_and_excluded_tags_that_do_not_overlap()
        {
            var resource = Resource();
            resource.Tags = new HashSet<int> { 1 };
            resource.ExcludedTags = new HashSet<int> { 2 };

            _subject.Validate(resource).IsValid.Should().BeTrue();
        }

        [Test]
        public void should_map_excluded_tags_both_ways()
        {
            var resource = Resource();
            resource.ExcludedTags = new HashSet<int> { 2 };

            var model = resource.ToModel();

            model.ExcludedTags.Should().BeEquivalentTo(new[] { 2 });
            model.ToResource().ExcludedTags.Should().BeEquivalentTo(new[] { 2 });
        }

        private class TestController : ReleaseProfileController
        {
            public TestController(IReleaseProfileService releaseProfileService, IIndexerFactory indexerFactory)
                : base(releaseProfileService, indexerFactory)
            {
            }

            public ValidationResult Validate(ReleaseProfileResource resource)
            {
                return SharedValidator.Validate(resource);
            }
        }
    }
}
