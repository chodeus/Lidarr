using System;
using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.AutoTagging;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Download;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Music;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Profiles.Delay;
using NzbDrone.Core.Profiles.MetadataSources;
using NzbDrone.Core.Profiles.Releases;
using NzbDrone.Core.Profiles.Tagging;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Tags;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.TagTests
{
    [TestFixture]
    public class TagServiceFixture : CoreTest<TagService>
    {
        private Tag _tag;

        [SetUp]
        public void Setup()
        {
            _tag = new Tag { Id = 3, Label = "tag name" };

            Mocker.GetMock<ITagRepository>().Setup(s => s.Get(_tag.Id)).Returns(_tag);
            Mocker.GetMock<ITagRepository>().Setup(s => s.All()).Returns(new List<Tag> { _tag });

            Mocker.GetMock<IDelayProfileService>().Setup(s => s.AllForTag(It.IsAny<int>())).Returns(new List<DelayProfile>());
            Mocker.GetMock<IImportListFactory>().Setup(s => s.AllForTag(It.IsAny<int>())).Returns(new List<ImportListDefinition>());
            Mocker.GetMock<INotificationFactory>().Setup(s => s.AllForTag(It.IsAny<int>())).Returns(new List<NotificationDefinition>());
            Mocker.GetMock<IReleaseProfileService>().Setup(s => s.AllForTag(It.IsAny<int>())).Returns(new List<ReleaseProfile>());
            Mocker.GetMock<IReleaseProfileService>().Setup(s => s.AllExcludedForTag(It.IsAny<int>())).Returns(new List<ReleaseProfile>());
            Mocker.GetMock<IArtistService>().Setup(s => s.AllForTag(It.IsAny<int>())).Returns(new List<Artist>());
            Mocker.GetMock<IRootFolderService>().Setup(s => s.AllForTag(It.IsAny<int>())).Returns(new List<RootFolder>());
            Mocker.GetMock<IIndexerFactory>().Setup(s => s.AllForTag(It.IsAny<int>())).Returns(new List<IndexerDefinition>());
            Mocker.GetMock<IAutoTaggingService>().Setup(s => s.AllForTag(It.IsAny<int>())).Returns(new List<AutoTag>());
            Mocker.GetMock<IDownloadClientFactory>().Setup(s => s.AllForTag(It.IsAny<int>())).Returns(new List<DownloadClientDefinition>());
            Mocker.GetMock<ITaggingProfileService>().Setup(s => s.AllForTag(It.IsAny<int>())).Returns(new List<TaggingProfile>());
            Mocker.GetMock<IMetadataSourceProfileService>().Setup(s => s.AllForTag(It.IsAny<int>())).Returns(new List<MetadataSourceProfile>());

            Mocker.GetMock<IDelayProfileService>().Setup(s => s.All()).Returns(new List<DelayProfile>());
            Mocker.GetMock<IImportListFactory>().Setup(s => s.All()).Returns(new List<ImportListDefinition>());
            Mocker.GetMock<INotificationFactory>().Setup(s => s.All()).Returns(new List<NotificationDefinition>());
            Mocker.GetMock<IReleaseProfileService>().Setup(s => s.All()).Returns(new List<ReleaseProfile>());
            Mocker.GetMock<IArtistService>().Setup(s => s.GetAllArtistsTags()).Returns(new Dictionary<int, List<int>>());
            Mocker.GetMock<IRootFolderService>().Setup(s => s.All()).Returns(new List<RootFolder>());
            Mocker.GetMock<IIndexerFactory>().Setup(s => s.All()).Returns(new List<IndexerDefinition>());
            Mocker.GetMock<IAutoTaggingService>().Setup(s => s.All()).Returns(new List<AutoTag>());
            Mocker.GetMock<IDownloadClientFactory>().Setup(s => s.All()).Returns(new List<DownloadClientDefinition>());
            Mocker.GetMock<ITaggingProfileService>().Setup(s => s.All()).Returns(new List<TaggingProfile>());
            Mocker.GetMock<IMetadataSourceProfileService>().Setup(s => s.All()).Returns(new List<MetadataSourceProfile>());
        }

        private MetadataSourceProfile GivenMetadataSourceProfileUsingTheTag()
        {
            var profile = new MetadataSourceProfile { Id = 7, Name = "Profile Name", Tags = new HashSet<int> { _tag.Id } };

            Mocker.GetMock<IMetadataSourceProfileService>().Setup(s => s.AllForTag(_tag.Id)).Returns(new List<MetadataSourceProfile> { profile });
            Mocker.GetMock<IMetadataSourceProfileService>().Setup(s => s.All()).Returns(new List<MetadataSourceProfile> { profile });

            return profile;
        }

        private ReleaseProfile GivenReleaseProfileExcludingTheTag()
        {
            var releaseProfile = new ReleaseProfile { Id = 7, ExcludedTags = new HashSet<int> { _tag.Id } };

            Mocker.GetMock<IReleaseProfileService>().Setup(s => s.AllExcludedForTag(_tag.Id)).Returns(new List<ReleaseProfile> { releaseProfile });
            Mocker.GetMock<IReleaseProfileService>().Setup(s => s.All()).Returns(new List<ReleaseProfile> { releaseProfile });

            return releaseProfile;
        }

        [Test]
        public void should_list_metadata_source_profiles_using_the_tag_in_its_details()
        {
            var profile = GivenMetadataSourceProfileUsingTheTag();

            var details = Subject.Details(_tag.Id);

            details.MetadataSourceProfileIds.Should().Equal(profile.Id);
            details.InUse.Should().BeTrue();
        }

        [Test]
        public void should_list_metadata_source_profiles_using_the_tag_in_all_details()
        {
            var profile = GivenMetadataSourceProfileUsingTheTag();

            Subject.Details().Should().ContainSingle().Which.MetadataSourceProfileIds.Should().Equal(profile.Id);
        }

        [Test]
        public void should_not_delete_a_tag_only_a_metadata_source_profile_uses()
        {
            GivenMetadataSourceProfileUsingTheTag();

            Action delete = () => Subject.Delete(_tag.Id);

            delete.Should().Throw<ModelConflictException>();
            Mocker.GetMock<ITagRepository>().Verify(s => s.Delete(It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void should_list_release_profiles_excluding_the_tag_in_its_details()
        {
            var releaseProfile = GivenReleaseProfileExcludingTheTag();

            var details = Subject.Details(_tag.Id);

            details.ExcludedReleaseProfileIds.Should().Equal(releaseProfile.Id);
            details.RestrictionIds.Should().BeEmpty();
            details.InUse.Should().BeTrue();
        }

        [Test]
        public void should_list_release_profiles_excluding_the_tag_in_all_details()
        {
            var releaseProfile = GivenReleaseProfileExcludingTheTag();

            var details = Subject.Details().Should().ContainSingle().Subject;

            details.ExcludedReleaseProfileIds.Should().Equal(releaseProfile.Id);
            details.RestrictionIds.Should().BeEmpty();
        }

        [Test]
        public void should_not_delete_a_tag_only_a_release_profile_excludes()
        {
            GivenReleaseProfileExcludingTheTag();

            Action delete = () => Subject.Delete(_tag.Id);

            delete.Should().Throw<ModelConflictException>();
            Mocker.GetMock<ITagRepository>().Verify(s => s.Delete(It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void should_start_tag_details_without_excluding_release_profiles()
        {
            new TagDetails().ExcludedReleaseProfileIds.Should().BeEmpty();
        }
    }
}
