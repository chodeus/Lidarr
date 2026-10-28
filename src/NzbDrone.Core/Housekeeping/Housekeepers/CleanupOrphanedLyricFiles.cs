using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Housekeeping.Housekeepers
{
    public class CleanupOrphanedLyricFiles : IHousekeepingTask
    {
        private readonly IMainDatabase _database;

        public CleanupOrphanedLyricFiles(IMainDatabase database)
        {
            _database = database;
        }

        public void Clean()
        {
            DeleteOrphanedByArtist();
            DeleteOrphanedByTrackFile();
            RelinkWhereTrackFileIsZero();
        }

        private void DeleteOrphanedByArtist()
        {
            using var mapper = _database.OpenConnection();
            mapper.Execute(@"DELETE FROM ""LyricFiles""
                             WHERE ""Id"" IN (
                             SELECT ""LyricFiles"".""Id"" FROM ""LyricFiles""
                             LEFT OUTER JOIN ""Artists""
                             ON ""LyricFiles"".""ArtistId"" = ""Artists"".""Id""
                             WHERE ""Artists"".""Id"" IS NULL)");
        }

        private void DeleteOrphanedByTrackFile()
        {
            using var mapper = _database.OpenConnection();
            mapper.Execute(@"DELETE FROM ""LyricFiles""
                             WHERE ""Id"" IN (
                             SELECT ""LyricFiles"".""Id"" FROM ""LyricFiles""
                             LEFT OUTER JOIN ""TrackFiles""
                             ON ""LyricFiles"".""TrackFileId"" = ""TrackFiles"".""Id""
                             WHERE ""LyricFiles"".""TrackFileId"" > 0
                             AND ""TrackFiles"".""Id"" IS NULL)");
        }

        private void RelinkWhereTrackFileIsZero()
        {
            UnlinkedExtraFiles.Relink(_database, "LyricFiles");
        }
    }
}
