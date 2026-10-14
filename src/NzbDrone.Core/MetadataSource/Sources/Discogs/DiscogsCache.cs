using System;
using System.Data.SQLite;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using NLog;
using NzbDrone.Common.Crypto;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Housekeeping;

namespace NzbDrone.Core.MetadataSource.Sources.Discogs
{
    public interface IDiscogsCache
    {
        bool TryGet<T>(string key, out T value)
            where T : new();

        void Set<T>(string key, T value, TimeSpan lifetime);

        void RemoveExpired();
    }

    // Its own file beside the database, so backups stay small; a cache that can't be used is skipped, never fatal
    public class DiscogsCache : IDiscogsCache
    {
        public const string FileName = "discogs-cache.db";

        // Changes when a Discogs resource gains or loses a field, so entries written by an older build are never read
        private static readonly string Shape = HashConverter.GetHashInt31(string.Join(";", typeof(DiscogsCache).Assembly.GetTypes()
            .Where(t => t.Namespace == typeof(DiscogsCache).Namespace && t.Name.EndsWith("Resource"))
            .SelectMany(t => t.GetProperties().Select(p => t.Name + "." + p.Name + ":" + p.PropertyType))
            .OrderBy(f => f, StringComparer.Ordinal))).ToString("x");

        private readonly string _connectionString;
        private readonly Logger _logger;
        private readonly object _lock = new();

        public DiscogsCache(IAppFolderInfo appFolderInfo, Logger logger)
        {
            _connectionString = new SQLiteConnectionStringBuilder
            {
                DataSource = Path.Combine(appFolderInfo.AppDataFolder, FileName),
                Version = 3,
                BusyTimeout = 1000
            }.ConnectionString;
            _logger = logger;
        }

        public bool TryGet<T>(string key, out T value)
            where T : new()
        {
            value = default;
            byte[] data = null;

            Run(command =>
            {
                command.CommandText = "SELECT Value FROM Entries WHERE Key = @key AND Expires > @now";
                command.Parameters.AddWithValue("@key", Stored(key));
                command.Parameters.AddWithValue("@now", DateTime.UtcNow.Ticks);
                data = command.ExecuteScalar() as byte[];
            });

            if (data == null)
            {
                return false;
            }

            try
            {
                value = Json.Deserialize<T>(Unzip(data));
                return value != null;
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Discarding unreadable Discogs cache entry {0}", key);
                return false;
            }
        }

        public void Set<T>(string key, T value, TimeSpan lifetime)
        {
            var data = Zip(value.ToJson());

            Run(command =>
            {
                command.CommandText = "INSERT OR REPLACE INTO Entries (Key, Value, Expires) VALUES (@key, @value, @expires)";
                command.Parameters.AddWithValue("@key", Stored(key));
                command.Parameters.AddWithValue("@value", data);
                command.Parameters.AddWithValue("@expires", DateTime.UtcNow.Add(lifetime).Ticks);
                command.ExecuteNonQuery();
            });
        }

        public void RemoveExpired()
        {
            Run(command =>
            {
                command.CommandText = "DELETE FROM Entries WHERE Expires <= @now";
                command.Parameters.AddWithValue("@now", DateTime.UtcNow.Ticks);
                command.ExecuteNonQuery();
            });
        }

        private void Run(Action<SQLiteCommand> action)
        {
            try
            {
                lock (_lock)
                {
                    using var connection = new SQLiteConnection(_connectionString);
                    connection.Open();

                    // Every time, so a cache file deleted while Lidarr runs is recreated
                    using var create = connection.CreateCommand();
                    create.CommandText = "CREATE TABLE IF NOT EXISTS Entries (Key TEXT PRIMARY KEY, Value BLOB NOT NULL, Expires INTEGER NOT NULL)";
                    create.ExecuteNonQuery();

                    using var command = connection.CreateCommand();
                    action(command);
                }
            }
            catch (Exception ex) when (ex is SQLiteException or IOException or UnauthorizedAccessException)
            {
                _logger.Warn(ex, "Discogs cache unavailable, asking Discogs directly");
            }
        }

        private static string Stored(string key) => Shape + "/" + key;

        private static byte[] Zip(string json)
        {
            using var output = new MemoryStream();

            using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
            {
                var bytes = Encoding.UTF8.GetBytes(json);
                gzip.Write(bytes, 0, bytes.Length);
            }

            return output.ToArray();
        }

        private static string Unzip(byte[] data)
        {
            using var gzip = new GZipStream(new MemoryStream(data), CompressionMode.Decompress);
            using var reader = new StreamReader(gzip, Encoding.UTF8);

            return reader.ReadToEnd();
        }
    }

    public class CleanupExpiredDiscogsCache : IHousekeepingTask
    {
        private readonly IDiscogsCache _cache;

        public CleanupExpiredDiscogsCache(IDiscogsCache cache)
        {
            _cache = cache;
        }

        public void Clean()
        {
            _cache.RemoveExpired();
        }
    }
}
