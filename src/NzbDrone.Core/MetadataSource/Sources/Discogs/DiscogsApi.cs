using System;
using System.Collections.Generic;
using System.Net;
using Newtonsoft.Json;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;

namespace NzbDrone.Core.MetadataSource.Sources.Discogs
{
    public interface IDiscogsApi
    {
        DiscogsArtistResource GetArtist(long id, string token);
        List<DiscogsArtistReleaseResource> GetArtistReleases(long id, string token);
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
        private const int MaxPages = 50;

        private static readonly TimeSpan TokenRateLimit = TimeSpan.FromMilliseconds(1050);
        private static readonly TimeSpan AnonymousRateLimit = TimeSpan.FromMilliseconds(2500);

        private readonly IHttpClient _httpClient;

        public DiscogsApi(IHttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public DiscogsArtistResource GetArtist(long id, string token)
        {
            return Get<DiscogsArtistResource>("artists/" + id, token);
        }

        public List<DiscogsArtistReleaseResource> GetArtistReleases(long id, string token)
        {
            return GetAll<DiscogsArtistReleasesPageResource, DiscogsArtistReleaseResource>("artists/" + id + "/releases", token, p => p.Releases);
        }

        public DiscogsMasterResource GetMaster(long id, string token)
        {
            return Get<DiscogsMasterResource>("masters/" + id, token);
        }

        public List<DiscogsVersionResource> GetMasterVersions(long id, string token)
        {
            return GetAll<DiscogsVersionsPageResource, DiscogsVersionResource>("masters/" + id + "/versions", token, p => p.Versions);
        }

        public DiscogsReleaseResource GetRelease(long id, string token)
        {
            return Get<DiscogsReleaseResource>("releases/" + id, token);
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

        private List<TItem> GetAll<TPage, TItem>(string resource, string token, Func<TPage, List<TItem>> items)
            where TPage : DiscogsPageResource, new()
        {
            var all = new List<TItem>();

            for (var page = 1; page <= MaxPages; page++)
            {
                var result = Get<TPage>(resource, token, ("page", page.ToString()), ("per_page", PageSize.ToString()));

                if (result == null)
                {
                    return page == 1 ? null : all;
                }

                all.AddRange(items(result));

                if (result.Pagination == null || page >= result.Pagination.Pages)
                {
                    break;
                }
            }

            return all;
        }

        // Null for a genuine not-found; anything else that goes wrong means Discogs is unavailable
        private T Get<T>(string resource, string token, params (string Key, string Value)[] query)
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

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return default;
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                throw new MetadataSourceUnavailableException("Discogs rejected the personal access token");
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
    }
}
