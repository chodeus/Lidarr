using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    [TestFixture]
    public class import_list_tag_existing_typeFixture : MigrationTest<import_list_tag_existing_type>
    {
        private void AddImportList(import_list_tag_existing_type c, string name, bool tagExisting)
        {
            c.Insert.IntoTable("ImportLists").Row(new
            {
                Name = name,
                Implementation = "LidarrLists",
                RootFolderPath = "/music",
                ShouldMonitor = 1,
                ProfileId = 1,
                MetadataProfileId = 1,
                TagExisting = tagExisting
            });
        }

        [Test]
        public void should_convert_tag_existing_to_type()
        {
            var db = WithMigrationTestDb(c =>
            {
                AddImportList(c, "Enabled", true);
                AddImportList(c, "Disabled", false);
            });

            var importLists = db.Query<ImportList914>("SELECT \"Name\", \"TagExisting\" FROM \"ImportLists\"");

            importLists.Should().HaveCount(2);
            importLists.Single(i => i.Name == "Enabled").TagExisting.Should().Be(1);
            importLists.Single(i => i.Name == "Disabled").TagExisting.Should().Be(0);
        }
    }

    public class ImportList914
    {
        public string Name { get; set; }
        public int TagExisting { get; set; }
    }
}
