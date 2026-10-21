using System;
using System.Collections.Generic;
using System.Net;
using Newtonsoft.Json;
using NzbDrone.Common.Http;

namespace NzbDrone.Core.MetadataSource.Sources.Deezer
{
    public interface IDeezerApi
    {
        DeezerArtistResource GetArtist(long id);
        List<DeezerAlbumResource> GetArtistAlbums(long id);
        DeezerAlbumResource GetAlbum(long id);
        List<DeezerTrackResource> GetAlbumTracks(long id);
        List<DeezerArtistResource> SearchArtists(string query, int limit);
        List<DeezerAlbumResource> SearchAlbums(string query, int limit);
    }

    // Deezer's public API, which needs no account; it allows 50 requests every 5 seconds
    public class DeezerApi : IDeezerApi
    {
        private const string BaseUrl = "https://api.deezer.com";
        private const int NotFoundCode = 800;
        private const int PageSize = 100;
        private const int MaxPages = 20;

        private static readonly TimeSpan RateLimit = TimeSpan.FromMilliseconds(110);

        private readonly IHttpClient _httpClient;

        public DeezerApi(IHttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public DeezerArtistResource GetArtist(long id)
        {
            return Get<DeezerArtistResource>("artist/" + id);
        }

        public List<DeezerAlbumResource> GetArtistAlbums(long id)
        {
            return GetAll<DeezerAlbumResource>("artist/" + id + "/albums");
        }

        public DeezerAlbumResource GetAlbum(long id)
        {
            return Get<DeezerAlbumResource>("album/" + id);
        }

        public List<DeezerTrackResource> GetAlbumTracks(long id)
        {
            return GetAll<DeezerTrackResource>("album/" + id + "/tracks");
        }

        public List<DeezerArtistResource> SearchArtists(string query, int limit)
        {
            return Get<DeezerPageResource<DeezerArtistResource>>("search/artist", ("q", query), ("limit", limit.ToString()))?.Data ?? new List<DeezerArtistResource>();
        }

        public List<DeezerAlbumResource> SearchAlbums(string query, int limit)
        {
            return Get<DeezerPageResource<DeezerAlbumResource>>("search/album", ("q", query), ("limit", limit.ToString()))?.Data ?? new List<DeezerAlbumResource>();
        }

        // Never a partial list: refresh would take it as complete and drop what is missing
        private List<T> GetAll<T>(string resource)
        {
            var items = new List<T>();

            for (var page = 0; page < MaxPages; page++)
            {
                var result = Get<DeezerPageResource<T>>(resource, ("index", (page * PageSize).ToString()), ("limit", PageSize.ToString()))
                    ?? throw new MetadataSourceUnavailableException("Deezer request for {0} failed: page {1} was not found", resource, page + 1);

                if (result.Data == null || (result.Data.Count == 0 && result.Next != null))
                {
                    throw new MetadataSourceUnavailableException("Deezer returned page {0} of {1} without its items", page + 1, resource);
                }

                items.AddRange(result.Data);

                if (result.Next != null)
                {
                    continue;
                }

                // Deezer counts the whole list, so fewer items than that is a short answer, not the end
                if (items.Count < result.Total)
                {
                    throw new MetadataSourceUnavailableException("Deezer listed {0} of {1} items for {2}", items.Count, result.Total, resource);
                }

                return items;
            }

            throw new MetadataSourceUnavailableException("Deezer request for {0} failed: more than {1} pages", resource, MaxPages);
        }

        // Null for a genuine not-found; anything else that goes wrong means Deezer is unavailable
        private T Get<T>(string resource, params (string Key, string Value)[] query)
            where T : DeezerResource, new()
        {
            var builder = new HttpRequestBuilder(BaseUrl).Resource(resource);

            foreach (var (key, value) in query)
            {
                builder.AddQueryParam(key, value);
            }

            var request = builder.Build();
            request.RateLimit = RateLimit;

            T result;

            try
            {
                result = _httpClient.Get<T>(request).Resource;
            }
            catch (Exception ex) when (ex is HttpException or WebException or JsonException)
            {
                throw new MetadataSourceUnavailableException("Deezer request for {0} failed: {1}", resource, ex.Message);
            }

            // An empty answer is no proof the item is gone: only Deezer's not-found code is
            if (result == null)
            {
                throw new MetadataSourceUnavailableException("Deezer returned an empty response for {0}", resource);
            }

            if (result.Error == null)
            {
                return result;
            }

            if (result.Error.Code == NotFoundCode)
            {
                return null;
            }

            throw new MetadataSourceUnavailableException("Deezer request for {0} failed: {1} ({2})", resource, result.Error.Message, result.Error.Code);
        }
    }
}
