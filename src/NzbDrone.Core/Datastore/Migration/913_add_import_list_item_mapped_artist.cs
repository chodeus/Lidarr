using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(913)]
    public class add_import_list_item_mapped_artist : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("ImportListItems").AddColumn("MappedArtistMusicBrainzId").AsString().Nullable();
        }
    }
}
