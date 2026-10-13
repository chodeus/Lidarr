using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(908)]
    public class album_metadata_missing_since : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("Albums").AddColumn("MetadataMissingSince").AsDateTimeOffset().Nullable();
        }
    }
}
