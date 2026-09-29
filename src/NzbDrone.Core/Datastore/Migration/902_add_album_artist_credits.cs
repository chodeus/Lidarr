using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(902)]
    public class add_album_artist_credits : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Create.TableForModel("AlbumArtistCredits")
                .WithColumn("AlbumId").AsInt32().Unique()
                .WithColumn("Credit").AsString().WithDefaultValue("")
                .WithColumn("Guests").AsString().WithDefaultValue("")
                .WithColumn("Artists").AsString().WithDefaultValue("[]")
                .WithColumn("LastFetched").AsDateTimeOffset();
        }
    }
}
