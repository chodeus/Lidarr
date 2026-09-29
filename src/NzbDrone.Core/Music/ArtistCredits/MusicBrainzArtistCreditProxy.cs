using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Http;

namespace NzbDrone.Core.Music.ArtistCredits
{
    public interface IMusicBrainzArtistCreditProxy
    {
        /// <summary>The release group's credit; empty when MusicBrainz has no such release group, null when it can't be read now.</summary>
        List<AlbumArtistCreditName> GetCredit(string releaseGroupId);
    }

    // Lidarr's metadata server keeps one artist per album, so credits come from MusicBrainz itself.
    public class MusicBrainzArtistCreditProxy : IMusicBrainzArtistCreditProxy
    {
        private static readonly string UserAgent = $"{BuildInfo.AppName}/{BuildInfo.Version} ( https://github.com/chodeus/Lidarr )";

        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;

        public MusicBrainzArtistCreditProxy(IHttpClient httpClient, Logger logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public List<AlbumArtistCreditName> GetCredit(string releaseGroupId)
        {
            try
            {
                // MusicBrainz allows one request per second per client.
                var request = new HttpRequestBuilder($"https://musicbrainz.org/ws/2/release-group/{releaseGroupId}")
                    .AddQueryParam("inc", "artist-credits")
                    .AddQueryParam("fmt", "json")
                    .SetHeader("User-Agent", UserAgent)
                    .WithRateLimit(1.1)
                    .Build();
                request.SuppressHttpError = true;
                request.RequestTimeout = TimeSpan.FromSeconds(15);

                var response = _httpClient.Get(request);

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return new List<AlbumArtistCreditName>();
                }

                if (response.StatusCode != HttpStatusCode.OK)
                {
                    _logger.Debug("MusicBrainz returned {0} for release group {1}", (int)response.StatusCode, releaseGroupId);
                    return null;
                }

                return Parse(response.Content);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Unable to read the artist credit of release group {0}", releaseGroupId);
                return null;
            }
        }

        public static List<AlbumArtistCreditName> Parse(string json)
        {
            using var doc = JsonDocument.Parse(json);

            return doc.RootElement.GetProperty("artist-credit").EnumerateArray()
                .Select(c => new AlbumArtistCreditName
                {
                    Name = c.GetProperty("name").GetString(),
                    ForeignArtistId = c.GetProperty("artist").GetProperty("id").GetString(),
                    JoinPhrase = c.TryGetProperty("joinphrase", out var join) ? join.GetString() ?? string.Empty : string.Empty
                })
                .ToList();
        }
    }
}
