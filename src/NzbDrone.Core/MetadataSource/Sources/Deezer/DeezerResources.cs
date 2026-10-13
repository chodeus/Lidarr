using System.Collections.Generic;
using Newtonsoft.Json;

namespace NzbDrone.Core.MetadataSource.Sources.Deezer
{
    public class DeezerErrorResource
    {
        public string Type { get; set; }
        public string Message { get; set; }
        public int Code { get; set; }
    }

    // Deezer answers errors, including not-found, with HTTP 200 and an error object
    public class DeezerResource
    {
        public DeezerErrorResource Error { get; set; }
    }

    public class DeezerPageResource<T> : DeezerResource
    {
        public List<T> Data { get; set; } = new List<T>();
        public int Total { get; set; }
        public string Next { get; set; }
    }

    public class DeezerArtistResource : DeezerResource
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public string Link { get; set; }

        [JsonProperty("picture_xl")]
        public string PictureXl { get; set; }
    }

    public class DeezerGenreResource
    {
        public string Name { get; set; }
    }

    public class DeezerAlbumResource : DeezerResource
    {
        public long Id { get; set; }
        public string Title { get; set; }
        public string Link { get; set; }
        public string Label { get; set; }
        public DeezerArtistResource Artist { get; set; }
        public DeezerPageResource<DeezerGenreResource> Genres { get; set; }

        [JsonProperty("cover_xl")]
        public string CoverXl { get; set; }

        [JsonProperty("release_date")]
        public string ReleaseDate { get; set; }

        [JsonProperty("record_type")]
        public string RecordType { get; set; }

        [JsonProperty("nb_tracks")]
        public int TrackCount { get; set; }
    }

    public class DeezerTrackResource
    {
        public long Id { get; set; }
        public string Title { get; set; }
        public int Duration { get; set; }
        public DeezerArtistResource Artist { get; set; }

        [JsonProperty("disk_number")]
        public int DiskNumber { get; set; }

        [JsonProperty("track_position")]
        public int TrackPosition { get; set; }
    }
}
