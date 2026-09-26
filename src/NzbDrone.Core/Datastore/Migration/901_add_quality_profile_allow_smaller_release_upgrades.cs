using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(901)]
    public class add_quality_profile_allow_smaller_release_upgrades : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("QualityProfiles").AddColumn("AllowSmallerReleaseUpgrades").AsBoolean().WithDefaultValue(false);
        }
    }
}
