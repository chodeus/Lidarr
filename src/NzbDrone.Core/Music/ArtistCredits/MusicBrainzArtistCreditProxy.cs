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
            // Albums from other metadata sources carry ids MusicBrainz has never issued.
            if (!Guid.TryParse(releaseGroupId, out var id))
            {
                return new List<AlbumArtistCreditName>();
            }

            try
            {
                // MusicBrainz allows one request per second per client.
                var request = new HttpRequestBuilder($"https://musicbrainz.org/ws/2/release-group/{id:D}")
                    .AddQueryParam("inc", "artist-credits")
                    .AddQueryParam("fmt", "json")
                    .SetHeader("User-Agent", UserAgent)
                    .WithRateLimit(1.1)
                    .Build();
                request.SuppressHttpError = true;
                request.AllowAutoRedirect = false;
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

            // A release group without a credit is an answer, not a failure: storing it stops the retries.
            if (!doc.RootElement.TryGetProperty("artist-credit", out var credits) || credits.ValueKind != JsonValueKind.Array)
            {
                return new List<AlbumArtistCreditName>();
            }

            return credits.EnumerateArray()
                .Select(c => new AlbumArtistCreditName
                {
                    Name = Text(c, "name"),
                    ForeignArtistId = c.TryGetProperty("artist", out var artist) ? Text(artist, "id") : string.Empty,
                    JoinPhrase = Text(c, "joinphrase")
                })
                .ToList();
        }

        private static string Text(JsonElement element, string property)
        {
            return element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : string.Empty;
        }
    }
}
