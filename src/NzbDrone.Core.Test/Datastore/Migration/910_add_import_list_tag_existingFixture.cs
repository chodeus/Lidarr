using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    [TestFixture]
    public class add_import_list_tag_existingFixture : MigrationTest<add_import_list_tag_existing>
    {
        [Test]
        public void should_leave_existing_lists_not_tagging_existing_artists()
        {
            var db = WithMigrationTestDb(c =>
            {
                c.Insert.IntoTable("ImportLists").Row(new
                {
                    Name = "List Name",
                    Implementation = "LidarrLists",
                    RootFolderPath = "/music",
                    ShouldMonitor = 1,
                    ProfileId = 1,
                    MetadataProfileId = 1
                });
            });

            var lists = db.Query<ImportList910>("SELECT \"TagExisting\" FROM \"ImportLists\"");

            lists.Should().ContainSingle();
            lists.First().TagExisting.Should().BeFalse();
        }
    }

    public class ImportList910
    {
        public bool TagExisting { get; set; }
    }
}
