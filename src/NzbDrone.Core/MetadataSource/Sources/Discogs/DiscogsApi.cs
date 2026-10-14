using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Newtonsoft.Json;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Common.TPL;

namespace NzbDrone.Core.MetadataSource.Sources.Discogs
{
    public interface IDiscogsApi
    {
        DiscogsArtistResource GetArtist(long id, string token);
        List<DiscogsArtistReleaseResource> GetArtistOwnReleases(long id, string token);
        List<DiscogsArtistReleaseResource> GetArtistBootlegs(long id, string token);
        DiscogsMasterResource GetMaster(long id, string token);
        List<DiscogsVersionResource> GetMasterVersions(long id, string token);
        DiscogsReleaseResource GetRelease(long id, string token);
        List<DiscogsSearchResultResource> Search(string type, string query, string artist, int limit, string token);
    }

    // Discogs allows 60 requests a minute with a personal access token and 25 without
    public class DiscogsApi : IDiscogsApi
    {
        private const string BaseUrl = "https://api.discogs.com";
        private const int PageSize = 100;
        private const int MaxPages = 100;
        public const string BootlegRole = "UnofficialRelease";
        private const string OwnRole = "Main";

        private static readonly TimeSpan TokenRateLimit = TimeSpan.FromMilliseconds(1050);
        private static readonly TimeSpan AnonymousRateLimit = TimeSpan.FromMilliseconds(2500);
        private static readonly TimeSpan RateWindow = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromMinutes(2);

        private readonly IHttpClient _httpClient;
        private readonly IRateLimitService _rateLimitService;

        public DiscogsApi(IHttpClient httpClient, IRateLimitService rateLimitService)
        {
            _httpClient = httpClient;
            _rateLimitService = rateLimitService;
        }

        public DiscogsArtistResource GetArtist(long id, string token)
        {
            return Get<DiscogsArtistResource>("artists/" + id, token);
        }

        public List<DiscogsArtistReleaseResource> GetArtistOwnReleases(long id, string token)
        {
            var resource = "artists/" + id + "/releases";

            return ReadOwnReleases(resource, token)
                ?? GetAll<DiscogsArtistReleasesPageResource, DiscogsArtistReleaseResource>(resource, token, p => p.Releases).Where(IsOwn).ToList();
        }

        public List<DiscogsArtistReleaseResource> GetArtistBootlegs(long id, string token)
        {
            var resource = "artists/" + id + "/releases";

            return ReadBootlegs(resource, token)
                ?? GetAll<DiscogsArtistReleasesPageResource, DiscogsArtistReleaseResource>(resource, token, p => p.Releases).Where(IsBootleg).ToList();
        }

        public DiscogsMasterResource GetMaster(long id, string token)
        {
            return Credited(Get<DiscogsMasterResource>("masters/" + id, token), "masters/" + id, m => m.Artists);
        }

        public List<DiscogsVersionResource> GetMasterVersions(long id, string token)
        {
            return GetAll<DiscogsVersionsPageResource, DiscogsVersionResource>("masters/" + id + "/versions", token, p => p.Versions);
        }

        public DiscogsReleaseResource GetRelease(long id, string token)
        {
            return Credited(Get<DiscogsReleaseResource>("releases/" + id, token), "releases/" + id, r => r.Artists);
        }

        public List<DiscogsSearchResultResource> Search(string type, string query, string artist, int limit, string token)
        {
            var parameters = new List<(string, string)> { ("type", type), ("q", query), ("per_page", limit.ToString()) };

            if (artist.IsNotNullOrWhiteSpace())
            {
                parameters.Add(("artist", artist));
            }

            return Get<DiscogsSearchPageResource>("database/search", token, parameters.ToArray())?.Results ?? new List<DiscogsSearchResultResource>();
        }

        // Discogs lists an artist's own releases before every other credit, so reading stops at the first other one.
        // Null when the list breaks that order, so the caller reads all of it
        private List<DiscogsArtistReleaseResource> ReadOwnReleases(string resource, string token)
        {
            var own = new List<DiscogsArtistReleaseResource>();

            for (var page = 1; page <= MaxPages; page++)
            {
                var result = GetPage<DiscogsArtistReleasesPageResource>(resource, token, page);
                var ownOnPage = result.Releases.TakeWhile(IsOwn).ToList();
                var others = result.Releases.Skip(ownOnPage.Count).ToList();
                var lastPage = result.Pagination == null || page >= result.Pagination.Pages;

                if (others.Any(IsOwn) || (page == 1 && ownOnPage.Count == 0 && !lastPage))
                {
                    return null;
                }

                // One look past the limit, so an artist with too many releases of their own fails without reading them all
                if (page == 1 && result.Pagination?.Pages > MaxPages && GetPage<DiscogsArtistReleasesPageResource>(resource, token, MaxPages + 1).Releases.Take(1).Any(IsOwn))
                {
                    throw TooManyPages("own releases", resource);
                }

                own.AddRange(ownOnPage);

                if (others.Any() || lastPage)
                {
                    return own;
                }
            }

            throw TooManyPages("own releases", resource);
        }

        // Bootlegs come after every other credit, so reading starts at the last page and stops at the first other one.
        // Null when the list breaks that order, so the caller reads all of it
        private List<DiscogsArtistReleaseResource> ReadBootlegs(string resource, string token)
        {
            var first = GetPage<DiscogsArtistReleasesPageResource>(resource, token, 1);
            var lastPage = Math.Max(first.Pagination?.Pages ?? 1, 1);
            var bootlegs = new List<DiscogsArtistReleaseResource>();

            for (var page = lastPage; page >= 1 && page > lastPage - MaxPages; page--)
            {
                var releases = page == 1 ? first.Releases : GetPage<DiscogsArtistReleasesPageResource>(resource, token, page).Releases;
                var bootlegsOnPage = releases.AsEnumerable().Reverse().TakeWhile(IsBootleg).Reverse().ToList();
                var others = releases.Take(releases.Count - bootlegsOnPage.Count).ToList();

                if (others.Any(IsBootleg))
                {
                    return null;
                }

                bootlegs.InsertRange(0, bootlegsOnPage);

                if (others.Any() || page == 1)
                {
                    return bootlegs;
                }
            }

            throw TooManyPages("bootlegs", resource);
        }

        // Discogs credits every master and release, if only as "Unknown Artist", so one without a credit is an unreadable answer
        private static T Credited<T>(T resource, string path, Func<T, List<DiscogsArtistCreditResource>> artists)
            where T : class
        {
            return resource == null || artists(resource)?.Any() == true ? resource : throw new MetadataSourceUnavailableException("Discogs returned {0} without an artist", path);
        }

        private static bool IsOwn(DiscogsArtistReleaseResource release)
        {
            return release.Role == OwnRole;
        }

        private static bool IsBootleg(DiscogsArtistReleaseResource release)
        {
            return release.Role == BootlegRole;
        }

        private static MetadataSourceUnavailableException TooManyPages(string section, string resource)
        {
            return new MetadataSourceUnavailableException("Discogs lists more than {0} pages of the artist's {1} for {2}, more than this source reads", MaxPages, section, resource);
        }

        // Never a partial list: refresh would take it as complete and drop what is missing
        private List<TItem> GetAll<TPage, TItem>(string resource, string token, Func<TPage, List<TItem>> items)
            where TPage : DiscogsPageResource, new()
        {
            var all = new List<TItem>();

            for (var page = 1; page <= MaxPages; page++)
            {
                var result = GetPage<TPage>(resource, token, page);

                // Checked on the first page, so a list too long to read costs one request
                if (result.Pagination?.Pages > MaxPages)
                {
                    throw new MetadataSourceUnavailableException("Discogs lists {0} pages for {1}, more than the {2} this source reads", result.Pagination.Pages, resource, MaxPages);
                }

                all.AddRange(items(result));

                if (result.Pagination == null || page >= result.Pagination.Pages)
                {
                    return all;
                }
            }

            throw new MetadataSourceUnavailableException("Discogs request for {0} failed: more than {1} pages", resource, MaxPages);
        }

        private TPage GetPage<TPage>(string resource, string token, int page)
            where TPage : DiscogsPageResource, new()
        {
            return Get<TPage>(resource, token, ("page", page.ToString()), ("per_page", PageSize.ToString()))
                ?? throw new MetadataSourceUnavailableException("Discogs request for {0} failed: page {1} was not found", resource, page);
        }

        // Null for a genuine not-found; anything else that goes wrong means Discogs is unavailable
        private T Get<T>(string resource, string token, params (string Key, string Value)[] query)
            where T : new()
        {
            var response = Send<T>(resource, token, query);

            // Other apps may share the token's limit, so a "too many requests" gets one retry after Discogs's wait
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                Pause(response, RetryAfter(response));
                response = Send<T>(resource, token, query);
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return default;
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                throw new DiscogsTokenRejectedException();
            }

            if (response.HasHttpError)
            {
                throw new MetadataSourceUnavailableException("Discogs request for {0} failed with HTTP {1}", resource, (int)response.StatusCode);
            }

            try
            {
                return response.Resource;
            }
            catch (JsonException ex)
            {
                throw new MetadataSourceUnavailableException("Discogs request for {0} returned an unreadable response: {1}", resource, ex.Message);
            }
        }

        private HttpResponse<T> Send<T>(string resource, string token, (string Key, string Value)[] query)
            where T : new()
        {
            var builder = new HttpRequestBuilder(BaseUrl).Resource(resource);

            foreach (var (key, value) in query)
            {
                builder.AddQueryParam(key, value);
            }

            var request = builder.Build();
            request.SuppressHttpError = true;
            request.RateLimit = token.IsNotNullOrWhiteSpace() ? TokenRateLimit : AnonymousRateLimit;

            // In a header, never the URL, so the token stays out of logs
            if (token.IsNotNullOrWhiteSpace())
            {
                request.Headers.Add("Authorization", "Discogs token=" + token);
            }

            HttpResponse<T> response;

            try
            {
                response = _httpClient.Get<T>(request);
            }
            catch (Exception ex) when (ex is HttpException or WebException or JsonException)
            {
                throw new MetadataSourceUnavailableException("Discogs request for {0} failed: {1}", resource, ex.Message);
            }

            // The limit is shared by everything using the token or address, so wait out the window once it runs out
            if (response.StatusCode != HttpStatusCode.TooManyRequests &&
                int.TryParse(response.Headers.GetSingleValue("X-Discogs-Ratelimit-Remaining"), out var remaining) && remaining <= 0)
            {
                Pause(response, RateWindow);
            }

            return response;
        }

        // The rate limiter holds the next request to this host until the pause is over
        private void Pause(HttpResponse response, TimeSpan pause)
        {
            _rateLimitService.WaitAndPulse(response.Request.Url.Host, pause);
        }

        private static TimeSpan RetryAfter(HttpResponse response)
        {
            return int.TryParse(response.Headers.GetSingleValue("Retry-After"), out var seconds) && seconds > 0
                ? TimeSpan.FromSeconds(Math.Min(seconds, MaxRetryAfter.TotalSeconds))
                : RateWindow;
        }
    }
}
