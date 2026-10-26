using System.Collections.Generic;
using FluentAssertions;
using Lidarr.Api.V1.ImportLists;
using Lidarr.Http.ClientSchema;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.LidarrLists;
using NzbDrone.Core.Localization;
using NzbDrone.Test.Common;

namespace NzbDrone.App.Test
{
    [TestFixture]
    public class ImportListResourceMapperFixture : TestBase
    {
        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<ILocalizationService>()
                .Setup(s => s.GetLocalizedString(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
                .Returns<string, Dictionary<string, object>>((s, d) => s);

            SchemaBuilder.Initialize(Mocker.Container);
        }

        private static ImportListResource Resource(TagExistingType? tagExisting)
        {
            return new ImportListResource { ConfigContract = nameof(LidarrListsSettings), Fields = new List<Field>(), TagExisting = tagExisting };
        }

        [Test]
        public void should_keep_the_stored_tag_existing_mode_when_a_save_leaves_it_out()
        {
            var existing = new ImportListDefinition { TagExisting = TagExistingType.Sync, Settings = new LidarrListsSettings() };

            new ImportListResourceMapper().ToModel(Resource(null), existing).TagExisting.Should().Be(TagExistingType.Sync);
        }

        [Test]
        public void should_save_the_tag_existing_mode_a_save_sends()
        {
            var existing = new ImportListDefinition { TagExisting = TagExistingType.Sync, Settings = new LidarrListsSettings() };

            new ImportListResourceMapper().ToModel(Resource(TagExistingType.Add), existing).TagExisting.Should().Be(TagExistingType.Add);
        }

        [Test]
        public void should_not_tag_existing_artists_for_a_new_list_that_leaves_the_mode_out()
        {
            new ImportListResourceMapper().ToModel(Resource(null), null).TagExisting.Should().Be(TagExistingType.None);
        }
    }
}
