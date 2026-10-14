using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.Music;

namespace NzbDrone.Core.MetadataSource.Sources.Discogs
{
    public record DiscogsFormatKind
    {
        public string PrimaryType { get; init; }
        public List<SecondaryAlbumType> SecondaryTypes { get; init; }
        public string ReleaseStatus { get; init; }
        public bool IsAudio { get; init; }
        public string MediaFormat { get; init; }
    }

    public static class DiscogsMapper
    {
        public const string IdPrefix = "discogs:";
        public const string Artist = "artist";
        public const string Master = "master";
        public const string Release = "release";

        private const string SiteUrl = "https://www.discogs.com";

        private static readonly Regex ForeignId = new Regex(@"^discogs:(artist|master|release):(\d+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Discogs numbers artists and labels that share a name, as in "Placeholder (2)"
        private static readonly Regex NameNumber = new Regex(@"\s\(\d+\)$", RegexOptions.Compiled);
        private static readonly Regex ProfileLink = new Regex(@"\[(?:a|l|r|m)=([^\]]+)\]", RegexOptions.Compiled);
        private static readonly Regex ProfileMarkup = new Regex(@"\[/?(?:a|l|r|m)?\d*(?:b|i|u|url[^\]]*)?\]", RegexOptions.Compiled);
        private static readonly Regex DiscTrack = new Regex(@"^[A-Za-z]{0,3}(\d+)[-.]\d+", RegexOptions.Compiled);
        private static readonly Regex VinylSide = new Regex(@"^([A-Z])\d*$", RegexOptions.Compiled);
        private static readonly Regex FormatQuantity = new Regex(@"^\d+x", RegexOptions.Compiled);

        // The artist list shortens Unofficial Release to Unofficial
        private static readonly HashSet<string> UnofficialDescriptions = new(StringComparer.OrdinalIgnoreCase) { "Unofficial Release", "Unofficial" };
        private static readonly HashSet<string> PromoDescriptions = new(StringComparer.OrdinalIgnoreCase) { "Promo", "Advance", "Smplr", "Sampler", "Test Pressing", "Transcription", "White Label" };

        private static readonly HashSet<string> NonAudioFormats = new(StringComparer.OrdinalIgnoreCase)
        {
            "VHS", "DVD", "DVDr", "Blu-ray", "Blu-ray-R", "HD DVD", "HD DVD-R", "Laserdisc", "Betamax", "Betacam", "Betacam SP",
            "U-matic", "Video 2000", "Video8", "MiniDV", "UMD", "VCD", "SVCD", "Floppy", "Floppy Disk"
        };

        public static string ArtistId(long id) => IdPrefix + Artist + ":" + id.ToString(CultureInfo.InvariantCulture);
        public static string MasterId(long id) => IdPrefix + Master + ":" + id.ToString(CultureInfo.InvariantCulture);
        public static string ReleaseId(long id) => IdPrefix + Release + ":" + id.ToString(CultureInfo.InvariantCulture);

        public static bool TryParseId(string foreignId, out string kind, out long id)
        {
            var match = ForeignId.Match(foreignId ?? string.Empty);

            kind = match.Success ? match.Groups[1].Value.ToLowerInvariant() : null;
            id = 0;

            return match.Success && long.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out id);
        }

        public static string WithoutNumber(string name) => NameNumber.Replace(name ?? string.Empty, string.Empty);

        // From an artist list entry such as "2xCD, EP, Promo": the first entry is the format, the rest describe it
        public static DiscogsFormatKind Classify(string formatList)
        {
            var parts = (formatList ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

            return Classify(parts.Take(1).Select(p => FormatQuantity.Replace(p, string.Empty)), parts.Skip(1));
        }

        public static DiscogsFormatKind Classify(List<DiscogsFormatResource> formats)
        {
            formats ??= new List<DiscogsFormatResource>();

            return Classify(formats.Select(f => f.Name), formats.SelectMany(f => f.Descriptions ?? new List<string>()));
        }

        public static DiscogsFormatKind Classify(IEnumerable<string> names, IEnumerable<string> descriptions)
        {
            var nameList = names.Where(n => n.IsNotNullOrWhiteSpace()).ToList();
            var described = new HashSet<string>(descriptions, StringComparer.OrdinalIgnoreCase);
            var audioName = nameList.FirstOrDefault(n => !NonAudioFormats.Contains(n));

            return new DiscogsFormatKind
            {
                PrimaryType = described.Contains("EP") ? PrimaryAlbumType.EP.Name : described.Contains("Single") || described.Contains("Maxi-Single") ? PrimaryAlbumType.Single.Name : PrimaryAlbumType.Album.Name,
                SecondaryTypes = described.Contains("Compilation") ? new List<SecondaryAlbumType> { SecondaryAlbumType.Compilation } : new List<SecondaryAlbumType>(),
                ReleaseStatus = described.Overlaps(UnofficialDescriptions) ? ReleaseStatus.Bootleg.Name : described.Overlaps(PromoDescriptions) ? ReleaseStatus.Promotion.Name : ReleaseStatus.Official.Name,
                IsAudio = !nameList.Any() || audioName != null,
                MediaFormat = audioName == null ? "Unknown" : audioName == "File" ? "Digital Media" : audioName
            };
        }

        public static ArtistMetadata MapArtist(DiscogsArtistResource resource)
        {
            var artist = NewArtist(resource.Id, resource.Name, resource.Uri);
            artist.Overview = Profile(resource.Profile);
            artist.Images = Images(MediaCoverTypes.Poster, resource.Images);

            return artist;
        }

        public static ArtistMetadata MapArtist(DiscogsArtistCreditResource credit)
        {
            return NewArtist(credit.Id, credit.Name, null);
        }

        public static ArtistMetadata MapArtist(DiscogsSearchResultResource result)
        {
            var artist = NewArtist(result.Id, result.Title, SiteUrl + result.Uri);
            artist.Images = Images(MediaCoverTypes.Poster, result.CoverImage);

            return artist;
        }

        public static Album MapAlbum(DiscogsArtistReleaseResource entry, DiscogsFormatKind kind)
        {
            var id = entry.Type == Master ? MasterId(entry.Id) : ReleaseId(entry.Id);

            return NewAlbum(id, entry.Title, ParseDate(null, entry.Year), kind, null, null, Images(MediaCoverTypes.Cover, entry.Thumb));
        }

        public static Album MapAlbum(DiscogsMasterResource master, DiscogsFormatKind kind)
        {
            return NewAlbum(MasterId(master.Id), master.Title, ParseDate(null, master.Year), kind, Genres(master.Genres, master.Styles), master.Uri, Images(MediaCoverTypes.Cover, master.Images));
        }

        public static Album MapAlbum(DiscogsReleaseResource release)
        {
            return NewAlbum(ReleaseId(release.Id), release.Title, ParseDate(release.Released, release.Year), Classify(release.Formats), Genres(release.Genres, release.Styles), release.Uri, Images(MediaCoverTypes.Cover, release.Images));
        }

        public static AlbumRelease MapRelease(DiscogsReleaseResource release, Dictionary<string, ArtistMetadata> artists, bool monitored)
        {
            var kind = Classify(release.Formats);
            var albumArtist = ArtistId(release.Artists.First().Id);

            var tracks = release.Tracklist
                .Where(t => t.Type.IsNullOrWhiteSpace() || t.Type == "track")
                .Select((t, index) => MapTrack(release.Id, t, index + 1, artists[t.Artists?.Any() == true ? ArtistId(t.Artists.First().Id) : albumArtist]))
                .ToList();

            return new AlbumRelease
            {
                ForeignReleaseId = ReleaseId(release.Id),
                OldForeignReleaseIds = new List<string>(),
                Title = release.Title,
                Status = kind.ReleaseStatus,
                Label = release.Labels?.Select(l => WithoutNumber(l.Name)).Distinct().ToList() ?? new List<string>(),
                Disambiguation = release.Labels?.FirstOrDefault()?.Catno ?? string.Empty,
                Country = release.Country.IsNullOrWhiteSpace() ? new List<string>() : new List<string> { release.Country },
                ReleaseDate = ParseDate(release.Released, release.Year),
                Tracks = tracks,
                TrackCount = tracks.Count,
                Duration = tracks.Sum(t => t.Duration),
                Media = tracks.Select(t => t.MediumNumber).Distinct().Select(n => new Medium { Number = n, Name = string.Empty, Format = kind.MediaFormat }).ToList(),
                Monitored = monitored
            };
        }

        // Search results carry no track list; adding the album loads it
        public static AlbumRelease MapSearchRelease(string foreignReleaseId, int trackCount, DiscogsFormatKind kind)
        {
            return new AlbumRelease
            {
                ForeignReleaseId = foreignReleaseId,
                OldForeignReleaseIds = new List<string>(),
                Status = kind.ReleaseStatus,
                Label = new List<string>(),
                Disambiguation = string.Empty,
                Country = new List<string>(),
                Tracks = new List<Track>(),
                TrackCount = trackCount,
                Media = new List<Medium> { new Medium { Number = 1, Name = string.Empty, Format = kind.MediaFormat } },
                Monitored = true
            };
        }

        public static IEnumerable<DiscogsArtistCreditResource> Credits(DiscogsReleaseResource release)
        {
            return release.Artists.Concat(release.Tracklist.SelectMany(t => t.Artists ?? new List<DiscogsArtistCreditResource>()));
        }

        // Vinyl sides pair up on a disc (A and B are disc 1), CD positions read disc-track
        public static int MediumNumber(string position)
        {
            var disc = DiscTrack.Match(position ?? string.Empty);

            if (disc.Success)
            {
                return Math.Max(int.Parse(disc.Groups[1].Value, CultureInfo.InvariantCulture), 1);
            }

            var side = VinylSide.Match(position ?? string.Empty);

            return side.Success ? ((side.Groups[1].Value[0] - 'A') / 2) + 1 : 1;
        }

        public static int DurationMs(string duration)
        {
            var parts = (duration ?? string.Empty).Split(':');

            if (parts.Length < 2 || parts.Length > 3 || !parts.All(p => int.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out _)))
            {
                return 0;
            }

            return parts.Aggregate(0, (total, part) => (total * 60) + int.Parse(part, CultureInfo.InvariantCulture)) * 1000;
        }

        private static Track MapTrack(long releaseId, DiscogsTrackResource track, int absoluteNumber, ArtistMetadata artist)
        {
            // Discogs has no track IDs, so the release ID and track index stand in
            var id = ReleaseId(releaseId) + ":" + absoluteNumber.ToString(CultureInfo.InvariantCulture);

            return new Track
            {
                ForeignTrackId = id,
                OldForeignTrackIds = new List<string>(),
                ForeignRecordingId = id,
                OldForeignRecordingIds = new List<string>(),
                Title = track.Title,
                TrackNumber = track.Position.IsNullOrWhiteSpace() ? absoluteNumber.ToString(CultureInfo.InvariantCulture) : track.Position,
                AbsoluteTrackNumber = absoluteNumber,
                Duration = DurationMs(track.Duration),
                MediumNumber = MediumNumber(track.Position),
                ArtistMetadata = artist
            };
        }

        private static ArtistMetadata NewArtist(long id, string name, string uri)
        {
            return new ArtistMetadata
            {
                ForeignArtistId = ArtistId(id),
                OldForeignArtistIds = new List<string>(),
                Name = WithoutNumber(name),
                Aliases = new List<string>(),
                Overview = string.Empty,
                Disambiguation = string.Empty,
                Type = string.Empty,
                Status = ArtistStatusType.Continuing,
                Genres = new List<string>(),
                Ratings = new Ratings(),
                Images = new List<MediaCover.MediaCover>(),
                Links = Links(uri)
            };
        }

        private static Album NewAlbum(string id, string title, DateTime? releaseDate, DiscogsFormatKind kind, List<string> genres, string uri, List<MediaCover.MediaCover> images)
        {
            return new Album
            {
                ForeignAlbumId = id,
                OldForeignAlbumIds = new List<string>(),
                Title = title,
                CleanTitle = Parser.Parser.CleanArtistName(title),
                Overview = string.Empty,
                Disambiguation = string.Empty,
                ReleaseDate = releaseDate,
                AlbumType = kind.PrimaryType,
                SecondaryTypes = kind.SecondaryTypes,
                Genres = genres ?? new List<string>(),
                Ratings = new Ratings(),
                Images = images,
                Links = Links(uri),
                AlbumReleases = new List<AlbumRelease>(),
                AnyReleaseOk = true
            };
        }

        // Discogs dates may be a year, a year and month, or carry 00 for unknown parts
        private static DateTime? ParseDate(string released, int year)
        {
            var parts = (released ?? string.Empty).Split('-').Select(p => int.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0).ToList();
            var y = parts.Count > 0 && parts[0] > 0 ? parts[0] : year;

            if (y <= 0)
            {
                return null;
            }

            var month = parts.Count > 1 && parts[1] is >= 1 and <= 12 ? parts[1] : 1;
            var day = parts.Count > 2 && parts[2] >= 1 && parts[2] <= DateTime.DaysInMonth(y, month) ? parts[2] : 1;

            return new DateTime(y, month, day, 0, 0, 0, DateTimeKind.Utc);
        }

        private static string Profile(string profile)
        {
            return ProfileMarkup.Replace(ProfileLink.Replace(profile ?? string.Empty, "$1"), string.Empty).Trim();
        }

        private static List<string> Genres(List<string> genres, List<string> styles)
        {
            return (genres ?? new List<string>()).Concat(styles ?? new List<string>()).Distinct().ToList();
        }

        private static List<MediaCover.MediaCover> Images(MediaCoverTypes type, List<DiscogsImageResource> images)
        {
            var image = images?.FirstOrDefault(i => i.Type == "primary") ?? images?.FirstOrDefault();

            return Images(type, image?.Uri);
        }

        // Discogs answers a missing image with a spacer
        private static List<MediaCover.MediaCover> Images(MediaCoverTypes type, string url)
        {
            return url.IsNullOrWhiteSpace() || url.EndsWith("spacer.gif", StringComparison.OrdinalIgnoreCase)
                ? new List<MediaCover.MediaCover>()
                : new List<MediaCover.MediaCover> { new MediaCover.MediaCover(type, url) };
        }

        private static List<Links> Links(string uri)
        {
            return uri.IsNullOrWhiteSpace() ? new List<Links>() : new List<Links> { new Links { Name = "Discogs", Url = uri } };
        }
    }
}
