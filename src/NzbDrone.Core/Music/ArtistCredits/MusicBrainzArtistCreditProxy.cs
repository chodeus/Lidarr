using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using NLog;
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
                var response = Get($"https://musicbrainz.org/ws/2/release-group/{id:D}?inc=artist-credits&fmt=json");

                // A merged release group's old id redirects to its new one: follow that, and only on MusicBrainz itself.
                if (IsRedirect(response.StatusCode))
                {
                    var location = response.Headers.GetSingleValue("Location");
                    if (MusicBrainzUrl(location) is not { } target)
                    {
                        _logger.Warn("MusicBrainz redirected release group {0} to {1}, which is not musicbrainz.org; not following it", releaseGroupId, location);
                        return null;
                    }

                    response = Get(target);
                }

                // 400, 404 and 410 are answers: stored as no credit, they can't hold up the queue on every run.
                switch (response.StatusCode)
                {
                    case HttpStatusCode.OK:
                        return Parse(response.Content);
                    case HttpStatusCode.BadRequest:
                    case HttpStatusCode.NotFound:
                    case HttpStatusCode.Gone:
                        return new List<AlbumArtistCreditName>();
                    default:
                        _logger.Debug("MusicBrainz returned {0} for release group {1}", (int)response.StatusCode, releaseGroupId);
                        return null;
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Unable to read the artist credit of release group {0}", releaseGroupId);
                return null;
            }
        }

        private HttpResponse Get(string url)
        {
            // MusicBrainz allows one request per second per client. No User-Agent header: Lidarr's dispatcher sets its own and rejects any other.
            var request = new HttpRequestBuilder(url)
                .WithRateLimit(1.1)
                .Build();
            request.SuppressHttpError = true;
            request.AllowAutoRedirect = false;
            request.RequestTimeout = TimeSpan.FromSeconds(15);

            return _httpClient.Get(request);
        }

        private static bool IsRedirect(HttpStatusCode status)
        {
            return status is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;
        }

        private static string MusicBrainzUrl(string location)
        {
            return Uri.TryCreate(location, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && uri.Host == "musicbrainz.org" && uri.IsDefaultPort ? uri.AbsoluteUri : null;
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
