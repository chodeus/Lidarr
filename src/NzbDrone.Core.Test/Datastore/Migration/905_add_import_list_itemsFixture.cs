using System;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    [TestFixture]
    public class add_import_list_itemsFixture : MigrationTest<add_import_list_items>
    {
        [Test]
        public void should_clear_last_sync_so_every_list_stores_its_items_first()
        {
            var db = WithMigrationTestDb(c =>
            {
                c.Insert.IntoTable("ImportListStatus").Row(new
                {
                    ProviderId = 1,
                    InitialFailure = (DateTime?)null,
                    MostRecentFailure = (DateTime?)null,
                    EscalationLevel = 0,
                    DisabledTill = (DateTime?)null,
                    LastInfoSync = new DateTime(2026, 9, 1)
                });
            });

            var statuses = db.Query<ImportListStatus905>("SELECT \"LastInfoSync\", \"HasRemovedItemSinceLastClean\" FROM \"ImportListStatus\"");

            statuses.Should().ContainSingle();
            statuses.First().LastInfoSync.Should().BeNull();
            statuses.First().HasRemovedItemSinceLastClean.Should().BeFalse();
        }
    }

    public class ImportListStatus905
    {
        public DateTime? LastInfoSync { get; set; }
        public bool HasRemovedItemSinceLastClean { get; set; }
    }
}
