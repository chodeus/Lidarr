using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.LidarrLists;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ImportListTests
{
    public class ImportListRepositoryFixture : DbTest<ImportListRepository, ImportListDefinition>
    {
        // Postgres refuses a mode in a yes/no column, so this also checks migration 914 converted it
        [TestCase(TagExistingType.None)]
        [TestCase(TagExistingType.Add)]
        [TestCase(TagExistingType.Sync)]
        public void should_store_each_tag_existing_mode(TagExistingType tagExisting)
        {
            var list = Builder<ImportListDefinition>.CreateNew().BuildNew();
            list.Settings = new LidarrListsSettings();
            list.ConfigContract = nameof(LidarrListsSettings);
            list.TagExisting = tagExisting;

            Subject.Insert(list);

            Subject.Single().TagExisting.Should().Be(tagExisting);
        }
    }
}
