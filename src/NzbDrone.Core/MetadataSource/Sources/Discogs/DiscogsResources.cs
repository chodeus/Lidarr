using System.Collections.Generic;
using Newtonsoft.Json;

namespace NzbDrone.Core.MetadataSource.Sources.Discogs
{
    public class DiscogsPaginationResource
    {
        public int Page { get; set; }

        // 0 is a real count; a missing one can't say whether the list is complete
        [JsonProperty(Required = Required.Always)]
        public int Pages { get; set; }
    }

    public class DiscogsPageResource
    {
        public DiscogsPaginationResource Pagination { get; set; }
    }

    public class DiscogsImageResource
    {
        public string Type { get; set; }
        public string Uri { get; set; }
    }

    public class DiscogsArtistCreditResource
    {
        public long Id { get; set; }
        public string Name { get; set; }
    }

    public class DiscogsArtistResource
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public string Profile { get; set; }
        public string Uri { get; set; }
        public List<DiscogsImageResource> Images { get; set; }
    }

    public class DiscogsArtistReleaseResource
    {
        public long Id { get; set; }
        public string Type { get; set; }
        public string Role { get; set; }
        public string Title { get; set; }
        public int Year { get; set; }
        public string Format { get; set; }
        public string Thumb { get; set; }

        [JsonProperty("main_release")]
        public long MainRelease { get; set; }
    }

    public class DiscogsArtistReleasesPageResource : DiscogsPageResource
    {
        public List<DiscogsArtistReleaseResource> Releases { get; set; } = new List<DiscogsArtistReleaseResource>();
    }

    public class DiscogsMasterResource
    {
        public long Id { get; set; }
        public string Title { get; set; }
        public int Year { get; set; }
        public string Uri { get; set; }
        public List<DiscogsArtistCreditResource> Artists { get; set; } = new List<DiscogsArtistCreditResource>();
        public List<DiscogsImageResource> Images { get; set; }
        public List<string> Genres { get; set; }
        public List<string> Styles { get; set; }
        public List<DiscogsTrackResource> Tracklist { get; set; } = new List<DiscogsTrackResource>();

        [JsonProperty("main_release")]
        public long MainRelease { get; set; }
    }

    public class DiscogsCommunityResource
    {
        [JsonProperty("in_collection")]
        public int InCollection { get; set; }
    }

    public class DiscogsVersionStatsResource
    {
        public DiscogsCommunityResource Community { get; set; }
    }

    public class DiscogsVersionResource
    {
        public long Id { get; set; }
        public DiscogsVersionStatsResource Stats { get; set; }

        public string Format { get; set; }

        [JsonProperty("major_formats")]
        public List<string> MajorFormats { get; set; }
    }

    public class DiscogsVersionsPageResource : DiscogsPageResource
    {
        public List<DiscogsVersionResource> Versions { get; set; } = new List<DiscogsVersionResource>();
    }

    public class DiscogsLabelResource
    {
        public string Name { get; set; }
        public string Catno { get; set; }
    }

    public class DiscogsFormatResource
    {
        public string Name { get; set; }
        public List<string> Descriptions { get; set; }
    }

    public class DiscogsTrackResource
    {
        public string Position { get; set; }
        public string Title { get; set; }
        public string Duration { get; set; }
        public List<DiscogsArtistCreditResource> Artists { get; set; }

        [JsonProperty("type_")]
        public string Type { get; set; }

        [JsonProperty("sub_tracks")]
        public List<DiscogsTrackResource> SubTracks { get; set; }
    }

    public class DiscogsReleaseResource
    {
        public long Id { get; set; }
        public string Title { get; set; }
        public int Year { get; set; }
        public string Released { get; set; }
        public string Country { get; set; }
        public string Uri { get; set; }
        public List<DiscogsArtistCreditResource> Artists { get; set; } = new List<DiscogsArtistCreditResource>();
        public List<DiscogsLabelResource> Labels { get; set; }
        public List<DiscogsFormatResource> Formats { get; set; }
        public List<DiscogsTrackResource> Tracklist { get; set; } = new List<DiscogsTrackResource>();
        public List<DiscogsImageResource> Images { get; set; }
        public List<string> Genres { get; set; }
        public List<string> Styles { get; set; }

        [JsonProperty("master_id")]
        public long MasterId { get; set; }
    }

    public class DiscogsSearchResultResource
    {
        public long Id { get; set; }
        public string Type { get; set; }
        public string Title { get; set; }
        public string Uri { get; set; }
        public List<string> Format { get; set; }

        [JsonProperty("cover_image")]
        public string CoverImage { get; set; }
    }

    public class DiscogsSearchPageResource : DiscogsPageResource
    {
        public List<DiscogsSearchResultResource> Results { get; set; } = new List<DiscogsSearchResultResource>();
    }
}
