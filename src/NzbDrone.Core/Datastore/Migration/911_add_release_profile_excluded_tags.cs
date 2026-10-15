using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(911)]
    public class add_release_profile_excluded_tags : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("ReleaseProfiles").AddColumn("ExcludedTags").AsString().NotNullable().WithDefaultValue("[]");
        }
    }
}
