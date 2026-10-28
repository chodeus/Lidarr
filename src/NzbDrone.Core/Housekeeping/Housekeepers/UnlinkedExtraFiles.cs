using System;
using System.Data;
using System.IO;
using System.Linq;
using Dapper;
using NzbDrone.Common;

namespace NzbDrone.Core.Housekeeping.Housekeepers
{
    // Rows an import stored with TrackFileId 0, before its track file had an id; each file was named after its track file
    public static class UnlinkedExtraFiles
    {
        public static void Relink(IDbConnection mapper, string table)
        {
            var unlinked = mapper.Query<UnlinkedRow>($@"SELECT ""{table}"".""Id"", ""{table}"".""RelativePath"", ""Artists"".""Path"" AS ""ArtistPath""
                                                        FROM ""{table}""
                                                        INNER JOIN ""Artists"" ON ""{table}"".""ArtistId"" = ""Artists"".""Id""
                                                        WHERE ""{table}"".""TrackFileId"" = 0").ToList();

            if (!unlinked.Any())
            {
                return;
            }

            var trackFilesByFolder = mapper.Query<TrackFileRow>(@"SELECT ""Id"", ""AlbumId"", ""Path"" FROM ""TrackFiles""")
                                           .ToLookup(f => Path.GetDirectoryName(f.Path), PathEqualityComparer.Instance);

            foreach (var row in unlinked)
            {
                var path = Path.Combine(row.ArtistPath, row.RelativePath);
                var name = Path.GetFileName(path);

                // The longest name wins, so "Mr. Blue Sky.lrc" goes to "Mr. Blue Sky.flac", not "Mr.flac"
                var trackFile = trackFilesByFolder[Path.GetDirectoryName(path)]
                    .Where(f => name.StartsWith(Path.GetFileNameWithoutExtension(f.Path) + ".", StringComparison.InvariantCultureIgnoreCase))
                    .OrderByDescending(f => f.Path.Length)
                    .FirstOrDefault();

                if (trackFile != null)
                {
                    mapper.Execute($@"UPDATE ""{table}"" SET ""TrackFileId"" = @TrackFileId, ""AlbumId"" = @AlbumId WHERE ""Id"" = @Id", new { TrackFileId = trackFile.Id, trackFile.AlbumId, row.Id });
                }
            }
        }

        private class UnlinkedRow
        {
            public int Id { get; set; }
            public string RelativePath { get; set; }
            public string ArtistPath { get; set; }
        }

        private class TrackFileRow
        {
            public int Id { get; set; }
            public int AlbumId { get; set; }
            public string Path { get; set; }
        }
    }
}
