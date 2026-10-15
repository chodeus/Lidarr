using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(912)]
    public class add_release_profile_indexer_ids : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("ReleaseProfiles").AddColumn("IndexerIds").AsString().NotNullable().WithDefaultValue("[]");

            Execute.Sql("UPDATE \"ReleaseProfiles\" SET \"IndexerIds\" = '[' || CAST(\"IndexerId\" AS TEXT) || ']' WHERE \"IndexerId\" > 0");
        }
    }
}
