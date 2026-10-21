using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Housekeeping.Housekeepers
{
    public class CleanupOrphanedImportListItems : IHousekeepingTask
    {
        private readonly IMainDatabase _database;

        public CleanupOrphanedImportListItems(IMainDatabase database)
        {
            _database = database;
        }

        public void Clean()
        {
            using var mapper = _database.OpenConnection();
            mapper.Execute(@"DELETE FROM ""ImportListItems""
                             WHERE ""Id"" IN (
                             SELECT ""ImportListItems"".""Id"" FROM ""ImportListItems""
                             LEFT OUTER JOIN ""ImportLists""
                             ON ""ImportListItems"".""ImportListId"" = ""ImportLists"".""Id""
                             WHERE ""ImportLists"".""Id"" IS NULL)");
        }
    }
}
