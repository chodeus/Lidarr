using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Music.ArtistCredits
{
    public class AlbumArtistCreditCandidate
    {
        public int AlbumId { get; set; }
        public string ForeignAlbumId { get; set; }
        public string ForeignArtistId { get; set; }
    }

    public interface IAlbumArtistCreditRepository : IBasicRepository<AlbumArtistCredit>
    {
        AlbumArtistCredit FindByAlbumId(int albumId);
        List<AlbumArtistCredit> FindByAlbumIds(List<int> albumIds);
        List<AlbumArtistCreditCandidate> GetCandidates(DateTime staleBefore, int limit);
        void DeleteByAlbumId(int albumId);
    }

    public class AlbumArtistCreditRepository : BasicRepository<AlbumArtistCredit>, IAlbumArtistCreditRepository
    {
        public AlbumArtistCreditRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public AlbumArtistCredit FindByAlbumId(int albumId)
        {
            return Query(c => c.AlbumId == albumId).SingleOrDefault();
        }

        public List<AlbumArtistCredit> FindByAlbumIds(List<int> albumIds)
        {
            return Query(c => Enumerable.Contains(albumIds, c.AlbumId));
        }

        // Albums with files come first (renames and tags need them), then albums sharing a title with a sibling (folders collide).
        public List<AlbumArtistCreditCandidate> GetCandidates(DateTime staleBefore, int limit)
        {
            const string sql = @"SELECT ""Albums"".""Id"" AS ""AlbumId"", ""Albums"".""ForeignAlbumId"", ""ArtistMetadata"".""ForeignArtistId""
                FROM ""Albums""
                JOIN ""ArtistMetadata"" ON ""ArtistMetadata"".""Id"" = ""Albums"".""ArtistMetadataId""
                LEFT JOIN ""AlbumArtistCredits"" ON ""AlbumArtistCredits"".""AlbumId"" = ""Albums"".""Id""
                WHERE ""AlbumArtistCredits"".""Id"" IS NULL OR ""AlbumArtistCredits"".""LastFetched"" < @StaleBefore
                ORDER BY
                    CASE WHEN ""AlbumArtistCredits"".""Id"" IS NULL THEN 0 ELSE 1 END,
                    CASE WHEN EXISTS (SELECT 1 FROM ""TrackFiles"" WHERE ""TrackFiles"".""AlbumId"" = ""Albums"".""Id"") THEN 0 ELSE 1 END,
                    CASE WHEN EXISTS (SELECT 1 FROM ""Albums"" AS ""Siblings""
                                      WHERE ""Siblings"".""ArtistMetadataId"" = ""Albums"".""ArtistMetadataId""
                                        AND ""Siblings"".""CleanTitle"" = ""Albums"".""CleanTitle""
                                        AND ""Siblings"".""Id"" <> ""Albums"".""Id"") THEN 0 ELSE 1 END,
                    ""AlbumArtistCredits"".""LastFetched"",
                    ""Albums"".""Added"" DESC
                LIMIT @Limit";

            using (var conn = _database.OpenConnection())
            {
                return conn.Query<AlbumArtistCreditCandidate>(sql, new { StaleBefore = staleBefore, Limit = limit }).ToList();
            }
        }

        public void DeleteByAlbumId(int albumId)
        {
            Delete(c => c.AlbumId == albumId);
        }
    }
}
