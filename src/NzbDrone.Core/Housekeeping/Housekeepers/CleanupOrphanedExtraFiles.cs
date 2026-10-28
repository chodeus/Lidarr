using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Housekeeping.Housekeepers
{
    public class CleanupOrphanedExtraFiles : IHousekeepingTask
    {
        private readonly IMainDatabase _database;

        public CleanupOrphanedExtraFiles(IMainDatabase database)
        {
            _database = database;
        }

        public void Clean()
        {
            DeleteOrphanedByArtist();
            DeleteOrphanedByTrackFile();
            DeleteWhereTrackFileIsZero();
        }

        private void DeleteOrphanedByArtist()
        {
            using var mapper = _database.OpenConnection();
            mapper.Execute(@"DELETE FROM ""ExtraFiles""
                             WHERE ""Id"" IN (
                             SELECT ""ExtraFiles"".""Id"" FROM ""ExtraFiles""
                             LEFT OUTER JOIN ""Artists""
                             ON ""ExtraFiles"".""ArtistId"" = ""Artists"".""Id""
                             WHERE ""Artists"".""Id"" IS NULL)");
        }

        private void DeleteOrphanedByTrackFile()
        {
            using var mapper = _database.OpenConnection();
            mapper.Execute(@"DELETE FROM ""ExtraFiles""
                             WHERE ""Id"" IN (
                             SELECT ""ExtraFiles"".""Id"" FROM ""ExtraFiles""
                             LEFT OUTER JOIN ""TrackFiles""
                             ON ""ExtraFiles"".""TrackFileId"" = ""TrackFiles"".""Id""
                             WHERE ""ExtraFiles"".""TrackFileId"" > 0
                             AND ""TrackFiles"".""Id"" IS NULL)");
        }

        // Rows an import stored before its track file had an id; the next artist scan imports their files again, linked
        private void DeleteWhereTrackFileIsZero()
        {
            using var mapper = _database.OpenConnection();
            mapper.Execute(@"DELETE FROM ""ExtraFiles""
                             WHERE ""Id"" IN (
                             SELECT ""Id"" FROM ""ExtraFiles""
                             WHERE ""TrackFileId"" = 0)");
        }
    }
}
