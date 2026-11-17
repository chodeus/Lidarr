using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dapper;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Music;

namespace NzbDrone.Core.Statistics
{
    public interface IStatisticsRepository
    {
        LibraryStatistics GetLibraryStatistics(StatisticsFilter filter = null);
    }

    public class StatisticsRepository : IStatisticsRepository
    {
        private const string _selectArtistsTemplate = "SELECT /**select**/ FROM \"Artists\" /**join**/ /**innerjoin**/ /**leftjoin**/ /**where**/ /**groupby**/ /**having**/ /**orderby**/";
        private const string _selectAlbumsTemplate = "SELECT /**select**/ FROM \"Albums\" /**join**/ /**innerjoin**/ /**leftjoin**/ /**where**/ /**groupby**/ /**having**/ /**orderby**/";
        private const string _selectTracksTemplate = "SELECT /**select**/ FROM \"Tracks\" /**join**/ /**innerjoin**/ /**leftjoin**/ /**where**/ /**groupby**/ /**having**/ /**orderby**/";
        private const string _selectTrackFilesTemplate = "SELECT /**select**/ FROM \"TrackFiles\" /**join**/ /**innerjoin**/ /**leftjoin**/ /**where**/ /**groupby**/ /**having**/ /**orderby**/";

        // Has-file comes from the join, as Wanted -> Missing does (AlbumRepository.AlbumsWithoutFilesBuilder)
        private const string _hasFile = @"""TrackFiles"".""Id"" IS NOT NULL";
        private const string _released = @"""Albums"".""ReleaseDate"" <= @currentDate";

        private readonly IMainDatabase _database;

        public StatisticsRepository(IMainDatabase database)
        {
            _database = database;
        }

        private string TrueIndicator => _database.DatabaseType == DatabaseType.PostgreSQL ? "true" : "1";

        private string MissingTrack => $@"""TrackFiles"".""Id"" IS NULL AND ""Albums"".""Monitored"" = {TrueIndicator} AND ""Artists"".""Monitored"" = {TrueIndicator} AND {_released}";

        public LibraryStatistics GetLibraryStatistics(StatisticsFilter filter = null)
        {
            var currentDate = DateTime.UtcNow;
            var artistFilter = BuildArtistFilter(filter);

            var artistCounts = QuerySingle<ArtistCounts>(ArtistsBuilder(artistFilter), _selectArtistsTemplate);
            var completedArtistCounts = QueryGrouped<CompletedArtistCounts>(
                TrackProgressBuilder(currentDate, artistFilter).GroupBy<Artist>(a => a.Id),
                _selectTracksTemplate,
                @"COALESCE(SUM(CASE WHEN ""TrackCount"" > 0 AND ""TrackCount"" = ""TrackFileCount"" THEN 1 ELSE 0 END), 0) AS CompletedArtistCount");
            var albumCounts = QuerySingle<AlbumCounts>(AlbumsBuilder(artistFilter), _selectAlbumsTemplate);
            var trackCounts = QueryGrouped<TrackCounts>(
                TracksByAlbumBuilder(currentDate, artistFilter),
                _selectTracksTemplate,
                @"COALESCE(SUM(CASE WHEN ""TrackCount"" > 0 AND ""TrackCount"" = ""TrackFileCount"" THEN 1 ELSE 0 END), 0) AS CompletedAlbumCount,
                  COALESCE(SUM(CASE WHEN ""MissingTrackCount"" > 0 THEN 1 ELSE 0 END), 0) AS MissingAlbumCount,
                  COALESCE(SUM(""TotalTrackCount""), 0) AS TotalTrackCount,
                  COALESCE(SUM(""MonitoredTrackCount""), 0) AS MonitoredTrackCount,
                  COALESCE(SUM(""TrackFileCount""), 0) AS DownloadedTrackCount,
                  COALESCE(SUM(""MissingTrackCount""), 0) AS MissingTrackCount,
                  COALESCE(SUM(""UnreleasedTrackCount""), 0) AS UnreleasedTrackCount");
            var trackFileCounts = QuerySingle<TrackFileCounts>(TrackFilesBuilder(artistFilter), _selectTrackFilesTemplate);

            return new LibraryStatistics
            {
                ArtistCount = artistCounts.ArtistCount,
                MonitoredArtistCount = artistCounts.MonitoredArtistCount,
                CompletedArtistCount = completedArtistCounts.CompletedArtistCount,
                ContinuingArtistCount = artistCounts.ContinuingArtistCount,
                EndedArtistCount = artistCounts.EndedArtistCount,
                DeletedArtistCount = artistCounts.DeletedArtistCount,
                AlbumCount = albumCounts.AlbumCount,
                MonitoredAlbumCount = albumCounts.MonitoredAlbumCount,
                CompletedAlbumCount = trackCounts.CompletedAlbumCount,
                MissingAlbumCount = trackCounts.MissingAlbumCount,
                TotalTrackCount = trackCounts.TotalTrackCount,
                MonitoredTrackCount = trackCounts.MonitoredTrackCount,
                DownloadedTrackCount = trackCounts.DownloadedTrackCount,
                MissingTrackCount = trackCounts.MissingTrackCount,
                UnreleasedTrackCount = trackCounts.UnreleasedTrackCount,
                TrackFileCount = trackFileCounts.TrackFileCount,
                SizeOnDisk = trackFileCounts.SizeOnDisk
            };
        }

        private ArtistFilter BuildArtistFilter(StatisticsFilter filter)
        {
            if (filter == null)
            {
                return null;
            }

            var conditions = new List<string>();
            var parameters = new DynamicParameters();
            var rootFolderPaths = filter.RootFolderPaths?.Where(p => p.IsNotNullOrWhiteSpace()).ToList();

            if (rootFolderPaths?.Count > 0)
            {
                var pathConditions = new List<string>();

                for (var i = 0; i < rootFolderPaths.Count; i++)
                {
                    // Trailing separator so '/music' doesn't match artists under '/music2'
                    var pathPrefix = rootFolderPaths[i].TrimEnd('/', '\\') + Path.DirectorySeparatorChar;

                    // SUBSTR, not LIKE, so characters in the path aren't wildcards; SQL LENGTH, as C# counts UTF-16 units
                    pathConditions.Add($@"SUBSTR(""Artists"".""Path"", 1, LENGTH(@pathPrefix{i})) = @pathPrefix{i}");
                    parameters.Add($"pathPrefix{i}", pathPrefix, null);
                }

                conditions.Add(BuildConditionGroup(pathConditions, filter.RootFolderPathsNot));
            }

            AddIdConditions(conditions, parameters, filter.TagIds, filter.TagIdsNot, "tagIdFilter", ArtistHasTagExpression);
            AddIdConditions(conditions, parameters, filter.QualityProfileIds, filter.QualityProfileIdsNot, "qualityProfileIdFilter", id => $@"""Artists"".""QualityProfileId"" = {id}");
            AddIdConditions(conditions, parameters, filter.MetadataProfileIds, filter.MetadataProfileIdsNot, "metadataProfileIdFilter", id => $@"""Artists"".""MetadataProfileId"" = {id}");

            if (filter.Monitored.HasValue)
            {
                conditions.Add(@"""Artists"".""Monitored"" = @monitoredFilter");
                parameters.Add("monitoredFilter", filter.Monitored.Value, null);
            }

            if (conditions.Count == 0)
            {
                return null;
            }

            return new ArtistFilter
            {
                Condition = string.Join(" AND ", conditions),
                Parameters = parameters
            };
        }

        private static void AddIdConditions(List<string> conditions, DynamicParameters parameters, List<int> ids, bool negate, string name, Func<string, string> condition)
        {
            if (ids == null || ids.Count == 0)
            {
                return;
            }

            var idConditions = new List<string>();

            for (var i = 0; i < ids.Count; i++)
            {
                idConditions.Add(condition($"@{name}{i}"));
                parameters.Add($"{name}{i}", ids[i], null);
            }

            conditions.Add(BuildConditionGroup(idConditions, negate));
        }

        private static string BuildConditionGroup(List<string> conditions, bool negate)
        {
            var group = $"({string.Join(" OR ", conditions)})";

            return negate ? $"NOT {group}" : group;
        }

        private string ArtistHasTagExpression(string tagId)
        {
            return _database.DatabaseType == DatabaseType.PostgreSQL
                ? $@"COALESCE(""Artists"".""Tags"", '[]')::jsonb @> jsonb_build_array({tagId})"
                : $@"EXISTS (SELECT 1 FROM json_each(COALESCE(""Artists"".""Tags"", '[]')) AS ""artistTag"" WHERE ""artistTag"".""value"" = {tagId})";
        }

        private static SqlBuilder ApplyArtistFilter(SqlBuilder builder, ArtistFilter artistFilter)
        {
            return artistFilter == null ? builder : builder.Where(artistFilter.Condition, artistFilter.Parameters);
        }

        private T QuerySingle<T>(SqlBuilder builder, string template)
        {
            var sql = builder.AddTemplate(template).LogQuery();

            using (var conn = _database.OpenConnection())
            {
                return conn.Query<T>(sql.RawSql, sql.Parameters).Single();
            }
        }

        private T QueryGrouped<T>(SqlBuilder builder, string template, string outerSelect)
        {
            return QuerySingle<T>(builder, $"SELECT {outerSelect} FROM ({template}) AS \"Grouped\"");
        }

        private SqlBuilder ArtistsBuilder(ArtistFilter artistFilter)
        {
            var parameters = new DynamicParameters();
            parameters.Add("continuing", (int)ArtistStatusType.Continuing, null);
            parameters.Add("ended", (int)ArtistStatusType.Ended, null);
            parameters.Add("deleted", (int)ArtistStatusType.Deleted, null);

            var builder = new SqlBuilder(_database.DatabaseType)
                .Select($@"COUNT(*) AS ArtistCount,
                           COALESCE(SUM(CASE WHEN ""Artists"".""Monitored"" = {TrueIndicator} THEN 1 ELSE 0 END), 0) AS MonitoredArtistCount,
                           COALESCE(SUM(CASE WHEN ""ArtistMetadata"".""Status"" = @continuing THEN 1 ELSE 0 END), 0) AS ContinuingArtistCount,
                           COALESCE(SUM(CASE WHEN ""ArtistMetadata"".""Status"" = @ended THEN 1 ELSE 0 END), 0) AS EndedArtistCount,
                           COALESCE(SUM(CASE WHEN ""ArtistMetadata"".""Status"" = @deleted THEN 1 ELSE 0 END), 0) AS DeletedArtistCount",
                    parameters)
                .Join<Artist, ArtistMetadata>((a, m) => a.ArtistMetadataId == m.Id);

            return ApplyArtistFilter(builder, artistFilter);
        }

        private SqlBuilder AlbumsBuilder(ArtistFilter artistFilter)
        {
            var builder = new SqlBuilder(_database.DatabaseType)
                .Select($@"COUNT(*) AS AlbumCount,
                           COALESCE(SUM(CASE WHEN ""Albums"".""Monitored"" = {TrueIndicator} THEN 1 ELSE 0 END), 0) AS MonitoredAlbumCount")
                .Join<Album, Artist>((album, artist) => album.ArtistMetadataId == artist.ArtistMetadataId);

            return ApplyArtistFilter(builder, artistFilter);
        }

        // Tracks on each album's monitored release, as ArtistStatisticsRepository counts them
        private SqlBuilder TrackScopeBuilder(DateTime currentDate, ArtistFilter artistFilter)
        {
            var parameters = new DynamicParameters();
            parameters.Add("currentDate", currentDate, null);

            var builder = new SqlBuilder(_database.DatabaseType)
                .AddParameters(parameters)
                .Join<Track, AlbumRelease>((t, r) => t.AlbumReleaseId == r.Id)
                .Join<AlbumRelease, Album>((r, a) => r.AlbumId == a.Id)
                .Join<Album, Artist>((album, artist) => album.ArtistMetadataId == artist.ArtistMetadataId)
                .LeftJoin<Track, TrackFile>((t, f) => t.TrackFileId == f.Id)
                .Where<AlbumRelease>(r => r.Monitored == true);

            return ApplyArtistFilter(builder, artistFilter);
        }

        // ArtistStatisticsRepository's TrackCount rule, except has-file comes from the join
        private SqlBuilder TrackProgressBuilder(DateTime currentDate, ArtistFilter artistFilter)
        {
            return TrackScopeBuilder(currentDate, artistFilter)
                .Select($@"SUM(CASE WHEN (""Albums"".""Monitored"" = {TrueIndicator} AND {_released}) OR {_hasFile} THEN 1 ELSE 0 END) AS ""TrackCount"",
                           SUM(CASE WHEN {_hasFile} THEN 1 ELSE 0 END) AS ""TrackFileCount"",
                           SUM(CASE WHEN {MissingTrack} THEN 1 ELSE 0 END) AS ""MissingTrackCount""");
        }

        private SqlBuilder TracksByAlbumBuilder(DateTime currentDate, ArtistFilter artistFilter)
        {
            return TrackProgressBuilder(currentDate, artistFilter)
                .Select($@"COUNT(*) AS ""TotalTrackCount"",
                           SUM(CASE WHEN ""Albums"".""Monitored"" = {TrueIndicator} THEN 1 ELSE 0 END) AS ""MonitoredTrackCount"",
                           SUM(CASE WHEN ""TrackFiles"".""Id"" IS NULL AND (""Albums"".""ReleaseDate"" IS NULL OR ""Albums"".""ReleaseDate"" > @currentDate) THEN 1 ELSE 0 END) AS ""UnreleasedTrackCount""")
                .GroupBy<Album>(a => a.Id);
        }

        private SqlBuilder TrackFilesBuilder(ArtistFilter artistFilter)
        {
            var builder = new SqlBuilder(_database.DatabaseType)
                .Select(@"COUNT(*) AS TrackFileCount,
                          COALESCE(SUM(COALESCE(""TrackFiles"".""Size"", 0)), 0) AS SizeOnDisk")
                .Join<TrackFile, Album>((f, a) => f.AlbumId == a.Id)
                .Join<Album, Artist>((album, artist) => album.ArtistMetadataId == artist.ArtistMetadataId);

            return ApplyArtistFilter(builder, artistFilter);
        }

        private class ArtistFilter
        {
            public string Condition { get; set; }
            public DynamicParameters Parameters { get; set; }
        }

        private class ArtistCounts
        {
            public int ArtistCount { get; set; }
            public int MonitoredArtistCount { get; set; }
            public int ContinuingArtistCount { get; set; }
            public int EndedArtistCount { get; set; }
            public int DeletedArtistCount { get; set; }
        }

        private class CompletedArtistCounts
        {
            public int CompletedArtistCount { get; set; }
        }

        private class AlbumCounts
        {
            public int AlbumCount { get; set; }
            public int MonitoredAlbumCount { get; set; }
        }

        private class TrackCounts
        {
            public int CompletedAlbumCount { get; set; }
            public int MissingAlbumCount { get; set; }
            public int TotalTrackCount { get; set; }
            public int MonitoredTrackCount { get; set; }
            public int DownloadedTrackCount { get; set; }
            public int MissingTrackCount { get; set; }
            public int UnreleasedTrackCount { get; set; }
        }

        private class TrackFileCounts
        {
            public int TrackFileCount { get; set; }
            public long SizeOnDisk { get; set; }
        }
    }
}
