using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(914)]
    public class import_list_tag_existing_type : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            IfDatabase("sqlite").Alter.Table("ImportLists").AlterColumn("TagExisting").AsInt32().NotNullable().WithDefaultValue(0);

            IfDatabase("postgres").Execute.Sql(
                "ALTER TABLE \"ImportLists\" ALTER COLUMN \"TagExisting\" DROP DEFAULT, " +
                "ALTER COLUMN \"TagExisting\" TYPE integer USING \"TagExisting\"::integer, " +
                "ALTER COLUMN \"TagExisting\" SET DEFAULT 0, " +
                "ALTER COLUMN \"TagExisting\" SET NOT NULL;");
        }
    }
}
