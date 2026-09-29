using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(903)]
    public class clear_album_guests_without_primary : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            // 902 stored the whole credit as guests when the album's own artist wasn't in it, and only then do the two match.
            Execute.Sql("UPDATE \"AlbumArtistCredits\" SET \"Guests\" = '' WHERE \"Guests\" = \"Credit\"");
        }
    }
}
