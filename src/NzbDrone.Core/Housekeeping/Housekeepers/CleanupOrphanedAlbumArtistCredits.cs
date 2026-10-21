using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Housekeeping.Housekeepers
{
    public class CleanupOrphanedAlbumArtistCredits : IHousekeepingTask
    {
        private readonly IMainDatabase _database;

        public CleanupOrphanedAlbumArtistCredits(IMainDatabase database)
        {
            _database = database;
        }

        public void Clean()
        {
            using var mapper = _database.OpenConnection();
            mapper.Execute(@"DELETE FROM ""AlbumArtistCredits""
                             WHERE ""Id"" IN (
                             SELECT ""AlbumArtistCredits"".""Id"" FROM ""AlbumArtistCredits""
                             LEFT OUTER JOIN ""Albums""
                             ON ""AlbumArtistCredits"".""AlbumId"" = ""Albums"".""Id""
                             WHERE ""Albums"".""Id"" IS NULL)");
        }
    }
}
