using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(905)]
    public class add_import_list_items : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Create.TableForModel("ImportListItems")
                .WithColumn("ImportListId").AsInt32()
                .WithColumn("Artist").AsString().Nullable()
                .WithColumn("ArtistMusicBrainzId").AsString().Nullable()
                .WithColumn("Album").AsString().Nullable()
                .WithColumn("AlbumMusicBrainzId").AsString().Nullable()
                .WithColumn("ReleaseDate").AsDateTimeOffset().Nullable();

            Create.Index().OnTable("ImportListItems").OnColumn("ImportListId");

            Alter.Table("ImportListStatus")
                .AddColumn("HasRemovedItemSinceLastClean").AsBoolean().WithDefaultValue(false);

            // Every list stores its items on its next sync before a clean can run
            Execute.Sql("UPDATE \"ImportListStatus\" SET \"LastInfoSync\" = NULL");
        }
    }
}
