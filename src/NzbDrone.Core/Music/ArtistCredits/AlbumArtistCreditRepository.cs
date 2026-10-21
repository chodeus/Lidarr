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
        public bool HasFiles { get; set; }
    }

    public interface IAlbumArtistCreditRepository : IBasicRepository<AlbumArtistCredit>
    {
        AlbumArtistCredit FindByAlbumId(int albumId);
        List<AlbumArtistCredit> FindByAlbumIds(List<int> albumIds);
        List<AlbumArtistCreditCandidate> GetCandidates(DateTime staleBefore, int limit);
        List<AlbumArtistCreditCandidate> GetUncreditedCandidates(List<int> albumIds);
        List<int> GetSiblingAlbumIds(int albumId);
        void DeleteByAlbumId(int albumId);
    }

    public class AlbumArtistCreditRepository : BasicRepository<AlbumArtistCredit>, IAlbumArtistCreditRepository
    {
        public AlbumArtistCreditRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        private const string CandidateColumns = @"SELECT ""Albums"".""Id"" AS ""AlbumId"", ""Albums"".""ForeignAlbumId"", ""ArtistMetadata"".""ForeignArtistId"",
                    EXISTS (SELECT 1 FROM ""TrackFiles"" WHERE ""TrackFiles"".""AlbumId"" = ""Albums"".""Id"") AS ""HasFiles""
                FROM ""Albums""
                JOIN ""ArtistMetadata"" ON ""ArtistMetadata"".""Id"" = ""Albums"".""ArtistMetadataId""
                LEFT JOIN ""AlbumArtistCredits"" ON ""AlbumArtistCredits"".""AlbumId"" = ""Albums"".""Id""";

        public AlbumArtistCredit FindByAlbumId(int albumId)
        {
            return FindByAlbumIds(new List<int> { albumId }).SingleOrDefault();
        }

        // Guests are worked out against the album's current artist, and a credit whose album is gone isn't returned
        public List<AlbumArtistCredit> FindByAlbumIds(List<int> albumIds)
        {
            var builder = Builder()
                .Join<AlbumArtistCredit, Album>((c, a) => c.AlbumId == a.Id)
                .Join<Album, ArtistMetadata>((a, m) => a.ArtistMetadataId == m.Id)
                .Where<AlbumArtistCredit>(c => Enumerable.Contains(albumIds, c.AlbumId));

            return _database.QueryJoined<AlbumArtistCredit, Album, ArtistMetadata>(builder, (credit, album, artist) =>
            {
                credit.Guests = credit.GuestsFor(artist.ForeignArtistId);
                return credit;
            }).ToList();
        }

        // Albums with files come first (renames and tags need them), then albums sharing a title with a sibling (folders collide).
        public List<AlbumArtistCreditCandidate> GetCandidates(DateTime staleBefore, int limit)
        {
            const string sql = CandidateColumns + @"
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

        public List<AlbumArtistCreditCandidate> GetUncreditedCandidates(List<int> albumIds)
        {
            // Integers written into the SQL, as WhereBuilderSqlite writes an id list
            var sql = CandidateColumns + $@"
                WHERE ""AlbumArtistCredits"".""Id"" IS NULL AND ""Albums"".""Id"" IN ({string.Join(", ", albumIds)})";

            using (var conn = _database.OpenConnection())
            {
                return conn.Query<AlbumArtistCreditCandidate>(sql).ToList();
            }
        }

        public List<int> GetSiblingAlbumIds(int albumId)
        {
            const string sql = @"SELECT ""Siblings"".""Id"" FROM ""Albums""
                JOIN ""Albums"" AS ""Siblings"" ON ""Siblings"".""ArtistMetadataId"" = ""Albums"".""ArtistMetadataId""
                    AND ""Siblings"".""CleanTitle"" = ""Albums"".""CleanTitle"" AND ""Siblings"".""Id"" <> ""Albums"".""Id""
                WHERE ""Albums"".""Id"" = @AlbumId";

            using (var conn = _database.OpenConnection())
            {
                return conn.Query<int>(sql, new { AlbumId = albumId }).ToList();
            }
        }

        public void DeleteByAlbumId(int albumId)
        {
            Delete(c => c.AlbumId == albumId);
        }
    }
}
