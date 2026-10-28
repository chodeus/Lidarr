using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using Dapper;
using NzbDrone.Common;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.MediaFiles;

namespace NzbDrone.Core.Housekeeping.Housekeepers
{
    // Rows an import stored with TrackFileId 0, before its track file had an id; each file was named after its track file
    public static class UnlinkedExtraFiles
    {
        public static void Relink(IMainDatabase database, string table)
        {
            using var mapper = database.OpenConnection();

            var unlinked = mapper.Query<UnlinkedRow>($@"SELECT ""{table}"".""Id"", ""{table}"".""RelativePath"", ""Artists"".""Path"" AS ""ArtistPath""
                                                        FROM ""{table}""
                                                        INNER JOIN ""Artists"" ON ""{table}"".""ArtistId"" = ""Artists"".""Id""
                                                        WHERE ""{table}"".""TrackFileId"" = 0").ToList();

            if (!unlinked.Any())
            {
                return;
            }

            foreach (var artist in unlinked.GroupBy(r => r.ArtistPath))
            {
                RelinkArtist(database, mapper, table, artist.Key, artist.ToList());
            }
        }

        // Only the artist's own track files, read as MediaFileRepository.GetFilesWithBasePath reads them
        private static void RelinkArtist(IMainDatabase database, IDbConnection mapper, string table, string artistPath, List<UnlinkedRow> unlinked)
        {
            var basePath = artistPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var trackFilesByFolder = database.Query<TrackFile>(new SqlBuilder(database.DatabaseType).Where<TrackFile>(x => x.Path.StartsWith(basePath)))
                                             .ToLookup(f => Path.GetDirectoryName(f.Path), PathEqualityComparer.Instance);

            var links = new List<object>();

            foreach (var row in unlinked)
            {
                var path = Path.Combine(row.ArtistPath, row.RelativePath);
                var name = Path.GetFileName(path);

                // The longest name wins, so "Mr. Blue Sky.lrc" goes to "Mr. Blue Sky.flac", not "Mr.flac"; a tie ("Song.flac" and
                // "Song.wav" for "Song.lrc") can't say which, so the row stays unlinked
                var matches = trackFilesByFolder[Path.GetDirectoryName(path)]
                    .Where(f => name.StartsWith(Path.GetFileNameWithoutExtension(f.Path) + ".", StringComparison.InvariantCultureIgnoreCase))
                    .GroupBy(f => Path.GetFileNameWithoutExtension(f.Path).Length)
                    .OrderByDescending(g => g.Key)
                    .FirstOrDefault()?
                    .ToList();

                if (matches?.Count == 1)
                {
                    links.Add(new { TrackFileId = matches[0].Id, matches[0].AlbumId, row.Id });
                }
            }

            using var transaction = mapper.BeginTransaction();
            mapper.Execute($@"UPDATE ""{table}"" SET ""TrackFileId"" = @TrackFileId, ""AlbumId"" = @AlbumId WHERE ""Id"" = @Id", links, transaction);
            transaction.Commit();
        }

        private class UnlinkedRow
        {
            public int Id { get; set; }
            public string RelativePath { get; set; }
            public string ArtistPath { get; set; }
        }
    }
}
