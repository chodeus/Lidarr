using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Housekeeping.Housekeepers;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Housekeeping.Housekeepers
{
    [TestFixture]
    public class CleanupOrphanedImportListItemsFixture : DbTest<CleanupOrphanedImportListItems, ImportListItemInfo>
    {
        [Test]
        public void should_delete_only_the_items_of_lists_that_are_gone()
        {
            var importList = Builder<ImportListDefinition>.CreateNew().BuildNew();
            Db.Insert(importList);

            Db.Insert(new ImportListItemInfo { ImportListId = importList.Id, Artist = "Kept" });
            Db.Insert(new ImportListItemInfo { ImportListId = importList.Id + 1, Artist = "Orphaned" });

            Subject.Clean();

            AllStoredModels.Should().ContainSingle(i => i.Artist == "Kept");
        }
    }
}
