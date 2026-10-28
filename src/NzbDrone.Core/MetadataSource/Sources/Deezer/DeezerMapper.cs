using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.Music;

namespace NzbDrone.Core.MetadataSource.Sources.Deezer
{
    public static class DeezerMapper
    {
        public const string IdPrefix = "deezer:";

        // Deezer sells digital releases only, so every album is one official release
        public static readonly string[] ReleaseStatuses = { "Official" };

        public static string ToForeignId(long id)
        {
            return IdPrefix + id.ToString(CultureInfo.InvariantCulture);
        }

        public static bool TryParseForeignId(string foreignId, out long id)
        {
            id = 0;

            return foreignId != null &&
                   foreignId.StartsWith(IdPrefix, StringComparison.OrdinalIgnoreCase) &&
                   long.TryParse(foreignId.AsSpan(IdPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out id);
        }

        public static ArtistMetadata MapArtist(DeezerArtistResource resource)
        {
            return new ArtistMetadata
            {
                ForeignArtistId = ToForeignId(resource.Id),
                OldForeignArtistIds = new List<string>(),
                Name = resource.Name,
                Aliases = new List<string>(),
                Overview = string.Empty,
                Disambiguation = string.Empty,
                Type = string.Empty,
                Status = ArtistStatusType.Continuing,
                Genres = new List<string>(),
                Ratings = new Ratings(),
                Images = Images(MediaCoverTypes.Poster, resource.PictureXl),
                Links = Links(resource.Link)
            };
        }

        public static Album MapAlbum(DeezerAlbumResource resource)
        {
            return new Album
            {
                ForeignAlbumId = ToForeignId(resource.Id),
                OldForeignAlbumIds = new List<string>(),
                Title = resource.Title,
                CleanTitle = Parser.Parser.CleanArtistName(resource.Title),
                Overview = string.Empty,
                Disambiguation = string.Empty,
                ReleaseDate = ParseDate(resource.ReleaseDate),
                AlbumType = PrimaryType(resource.RecordType),
                SecondaryTypes = resource.RecordType == "compile" ? new List<SecondaryAlbumType> { SecondaryAlbumType.Compilation } : new List<SecondaryAlbumType>(),
                Genres = resource.Genres?.Data?.Select(g => g.Name).ToList() ?? new List<string>(),
                Ratings = new Ratings(),
                Images = Images(MediaCoverTypes.Cover, resource.CoverXl),
                Links = Links(resource.Link),
                AlbumReleases = new List<AlbumRelease>(),
                AnyReleaseOk = true
            };
        }

        public static AlbumRelease MapRelease(DeezerAlbumResource resource, List<DeezerTrackResource> tracks, Dictionary<string, ArtistMetadata> artists)
        {
            var mapped = tracks.OrderBy(t => Math.Max(t.DiskNumber, 1))
                .ThenBy(t => t.TrackPosition)
                .Select((t, index) => MapTrack(t, index + 1, artists))
                .ToList();

            var release = NewRelease(resource);
            release.Tracks = mapped;
            release.TrackCount = mapped.Count;
            release.Duration = mapped.Sum(t => t.Duration);
            release.Media = mapped.Select(t => t.MediumNumber).Distinct().Select(Medium).ToList();

            return release;
        }

        // Search results carry no track list; adding the album loads it
        public static AlbumRelease MapSearchRelease(DeezerAlbumResource resource)
        {
            var release = NewRelease(resource);
            release.Tracks = new List<Track>();
            release.TrackCount = resource.TrackCount;
            release.Media = new List<Medium> { Medium(1) };

            return release;
        }

        private static AlbumRelease NewRelease(DeezerAlbumResource resource)
        {
            return new AlbumRelease
            {
                ForeignReleaseId = ToForeignId(resource.Id),
                OldForeignReleaseIds = new List<string>(),
                Title = resource.Title,
                Status = ReleaseStatuses[0],
                Label = resource.Label.IsNullOrWhiteSpace() ? new List<string>() : new List<string> { resource.Label },
                Disambiguation = string.Empty,
                Country = new List<string>(),
                ReleaseDate = ParseDate(resource.ReleaseDate),
                Monitored = true
            };
        }

        private static Track MapTrack(DeezerTrackResource resource, int absoluteNumber, Dictionary<string, ArtistMetadata> artists)
        {
            return new Track
            {
                ForeignTrackId = ToForeignId(resource.Id),
                OldForeignTrackIds = new List<string>(),
                ForeignRecordingId = ToForeignId(resource.Id),
                OldForeignRecordingIds = new List<string>(),
                Title = resource.Title,
                TrackNumber = resource.TrackPosition.ToString(CultureInfo.InvariantCulture),
                AbsoluteTrackNumber = absoluteNumber,
                Duration = resource.Duration * 1000,
                MediumNumber = Math.Max(resource.DiskNumber, 1),
                ArtistMetadata = artists[ToForeignId(resource.Artist.Id)]
            };
        }

        private static Medium Medium(int number)
        {
            return new Medium { Number = number, Name = string.Empty, Format = "Digital Media" };
        }

        private static string PrimaryType(string recordType)
        {
            return recordType switch
            {
                "single" => PrimaryAlbumType.Single.Name,
                "ep" => PrimaryAlbumType.EP.Name,
                _ => PrimaryAlbumType.Album.Name
            };
        }

        private static DateTime? ParseDate(string date)
        {
            return DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed) ? parsed : null;
        }

        private static List<MediaCover.MediaCover> Images(MediaCoverTypes type, string url)
        {
            return url.IsNullOrWhiteSpace() ? new List<MediaCover.MediaCover>() : new List<MediaCover.MediaCover> { new MediaCover.MediaCover(type, url) };
        }

        private static List<Links> Links(string url)
        {
            return url.IsNullOrWhiteSpace() ? new List<Links>() : new List<Links> { new Links { Name = "Deezer", Url = url } };
        }
    }
}
