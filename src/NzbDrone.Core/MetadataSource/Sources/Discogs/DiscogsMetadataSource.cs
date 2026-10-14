using System;
using System.Collections.Generic;
using System.Linq;
using FluentValidation;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Music;
using NzbDrone.Core.Profiles.Metadata;
using NzbDrone.Core.ThingiProvider;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.MetadataSource.Sources.Discogs
{
    public class DiscogsMetadataSourceSettingsValidator : AbstractValidator<DiscogsMetadataSourceSettings>
    {
        public DiscogsMetadataSourceSettingsValidator()
        {
            RuleFor(c => c.PressingsPerAlbum).GreaterThanOrEqualTo(0);
        }
    }

    public class DiscogsMetadataSourceSettings : IProviderConfig
    {
        private static readonly DiscogsMetadataSourceSettingsValidator Validator = new();

        [FieldDefinition(0, Label = "Personal Access Token", Privacy = PrivacyLevel.ApiKey, HelpText = "Optional, from Discogs Settings → Developers. Raises the limit from 25 to 60 requests a minute.")]
        public string Token { get; set; } = string.Empty;

        [FieldDefinition(1, Label = "Pressings Per Album", Type = FieldType.Number, HelpText = "The main release plus the most collected pressings. 0 loads every pressing; each pressing is one request.")]
        public int PressingsPerAlbum { get; set; } = 25;

        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }

    public class DiscogsMetadataSource : MetadataSourceBase<DiscogsMetadataSourceSettings>
    {
        private const int ArtistSearchLimit = 10;
        private const int AlbumSearchLimit = 5;

        private readonly IDiscogsApi _api;
        private readonly IArtistService _artistService;
        private readonly IAlbumService _albumService;
        private readonly IMetadataProfileService _metadataProfileService;
        private readonly ICached<DiscogsFormatKind> _mainReleaseKinds;
        private readonly Logger _logger;

        public DiscogsMetadataSource(IDiscogsApi api,
                                     IArtistService artistService,
                                     IAlbumService albumService,
                                     IMetadataProfileService metadataProfileService,
                                     ICacheManager cacheManager,
                                     Logger logger)
        {
            _api = api;
            _artistService = artistService;
            _albumService = albumService;
            _metadataProfileService = metadataProfileService;
            _mainReleaseKinds = cacheManager.GetCache<DiscogsFormatKind>(GetType(), "mainReleaseKinds");
            _logger = logger;
        }

        public override string Name => "Discogs";

        public override string IdFamily => MetadataIdFamilies.Discogs;

        public override bool CanHandleId(string foreignId) => DiscogsMapper.TryParseId(foreignId, out _, out _);

        public override ValidationResult Test()
        {
            try
            {
                _api.Search("artist", "test", null, 1, Settings.Token);
                return new ValidationResult();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Unable to reach Discogs");
                return new ValidationResult(new[] { new ValidationFailure(nameof(DiscogsMetadataSourceSettings.Token), "Unable to reach Discogs: " + ex.Message) });
            }
        }

        public override Artist GetArtistInfo(string foreignArtistId, int metadataProfileId)
        {
            var id = ParseId(foreignArtistId, DiscogsMapper.Artist);
            var resource = _api.GetArtist(id, Settings.Token) ?? throw new ArtistNotFoundException(foreignArtistId);
            var filter = MetadataProfileAlbumFilter.For(_metadataProfileService, metadataProfileId);

            var artist = new Artist { Metadata = DiscogsMapper.MapArtist(resource) };
            artist.CleanName = Parser.Parser.CleanArtistName(artist.Metadata.Value.Name);
            artist.SortName = Parser.Parser.NormalizeTitle(artist.Metadata.Value.Name);

            artist.Albums = _api.GetArtistOwnReleases(id, Settings.Token)
                .Select(entry => (Entry: entry, Kind: entry.Type == DiscogsMapper.Master ? MainReleaseKind(entry.MainRelease) : DiscogsMapper.Classify(entry.Format)))
                .Where(x => x.Kind.IsAudio && filter.Allows(x.Kind.PrimaryType, x.Kind.SecondaryTypes.Select(t => t.Name).ToList(), new[] { x.Kind.ReleaseStatus }))
                .Select(x => DiscogsMapper.MapAlbum(x.Entry, x.Kind))
                .ToList();

            return artist;
        }

        public override Tuple<string, Album, List<ArtistMetadata>> GetAlbumInfo(string foreignAlbumId)
        {
            if (!DiscogsMapper.TryParseId(foreignAlbumId, out var kind, out var id) || kind == DiscogsMapper.Artist)
            {
                throw new ArgumentException("Not a Discogs album ID: " + foreignAlbumId);
            }

            Album album;
            List<DiscogsReleaseResource> releases;
            long monitoredId;
            DiscogsArtistCreditResource albumArtist;

            if (kind == DiscogsMapper.Master)
            {
                var master = _api.GetMaster(id, Settings.Token) ?? throw new AlbumNotFoundException(foreignAlbumId);
                releases = Pressings(master).Select(r => _api.GetRelease(r, Settings.Token)).Where(r => r != null).ToList();
                monitoredId = releases.Any(r => r.Id == master.MainRelease) ? master.MainRelease : releases.FirstOrDefault()?.Id ?? 0;
                album = DiscogsMapper.MapAlbum(master, DiscogsMapper.Classify(releases.FirstOrDefault(r => r.Id == monitoredId)?.Formats));
                albumArtist = master.Artists.First();
            }
            else
            {
                var release = _api.GetRelease(id, Settings.Token) ?? throw new AlbumNotFoundException(foreignAlbumId);
                releases = new List<DiscogsReleaseResource> { release };
                monitoredId = release.Id;
                album = DiscogsMapper.MapAlbum(release);
                albumArtist = release.Artists.First();
            }

            var artists = releases.SelectMany(DiscogsMapper.Credits)
                .Prepend(albumArtist)
                .DistinctBy(a => a.Id)
                .Select(DiscogsMapper.MapArtist)
                .ToDictionary(a => a.ForeignArtistId);

            album.AlbumReleases = releases.Select(r => DiscogsMapper.MapRelease(r, artists, r.Id == monitoredId)).Where(r => r.TrackCount > 0).ToList();

            var artistId = DiscogsMapper.ArtistId(albumArtist.Id);
            album.ArtistMetadata = artists[artistId];

            return Tuple.Create(artistId, album, artists.Values.ToList());
        }

        public override List<Artist> SearchForNewArtist(string title)
        {
            List<ArtistMetadata> found;

            if (DiscogsMapper.TryParseId(title?.Trim(), out var kind, out var id))
            {
                var resource = kind == DiscogsMapper.Artist ? _api.GetArtist(id, Settings.Token) : null;
                found = resource == null ? new List<ArtistMetadata>() : new List<ArtistMetadata> { DiscogsMapper.MapArtist(resource) };
            }
            else
            {
                found = _api.Search("artist", title, null, ArtistSearchLimit, Settings.Token).Select(DiscogsMapper.MapArtist).ToList();
            }

            return found.Select(m => _artistService.FindById(m.ForeignArtistId) ?? new Artist { Metadata = m }).ToList();
        }

        public override List<Album> SearchForNewAlbum(string title, string artist)
        {
            if (DiscogsMapper.TryParseId(title?.Trim(), out var kind, out var id))
            {
                return kind switch
                {
                    DiscogsMapper.Master => new[] { _api.GetMaster(id, Settings.Token) }.Where(m => m != null).Select(m => MapSearchResult(m, null)).ToList(),
                    DiscogsMapper.Release => new[] { _api.GetRelease(id, Settings.Token) }.Where(r => r != null).Select(MapSearchResult).ToList(),
                    _ => new List<Album>()
                };
            }

            // A master search result names no artist ID, so each is read in full
            return _api.Search("master", title, artist, AlbumSearchLimit, Settings.Token)
                .Select(result => (Master: _api.GetMaster(result.Id, Settings.Token), Result: result))
                .Where(x => x.Master != null)
                .Select(x => MapSearchResult(x.Master, x.Result.Format))
                .ToList();
        }

        public override List<object> SearchForNewEntity(string title)
        {
            if (DiscogsMapper.TryParseId(title?.Trim(), out var kind, out _))
            {
                return kind == DiscogsMapper.Artist ? SearchForNewArtist(title).Cast<object>().ToList() : SearchForNewAlbum(title, null).Cast<object>().ToList();
            }

            return SearchForNewArtist(title).Cast<object>()
                .Concat(SearchForNewAlbum(title, null))
                .ToList();
        }

        // The main release and the most-owned pressings are the likeliest to match the files people have
        private List<long> Pressings(DiscogsMasterResource master)
        {
            var limit = Settings.PressingsPerAlbum;

            if (limit == 1)
            {
                return new List<long> { master.MainRelease };
            }

            var others = _api.GetMasterVersions(master.Id, Settings.Token)
                .Where(v => v.Id != master.MainRelease && DiscogsMapper.Classify(v.MajorFormats ?? new List<string>(), new List<string>()).IsAudio)
                .OrderByDescending(v => v.Stats?.Community?.InCollection ?? 0)
                .Select(v => v.Id);

            var pressings = others.Prepend(master.MainRelease);

            return (limit > 0 ? pressings.Take(limit) : pressings).ToList();
        }

        // Masters carry no format, so the album type comes from the main release
        private DiscogsFormatKind MainReleaseKind(long mainReleaseId)
        {
            return _mainReleaseKinds.Get(mainReleaseId.ToString(), () => DiscogsMapper.Classify(_api.GetRelease(mainReleaseId, Settings.Token)?.Formats), TimeSpan.FromDays(1));
        }

        private long ParseId(string foreignId, string expectedKind)
        {
            return DiscogsMapper.TryParseId(foreignId, out var kind, out var id) && kind == expectedKind ? id : throw new ArgumentException("Not a Discogs " + expectedKind + " ID: " + foreignId);
        }

        private Album MapSearchResult(DiscogsMasterResource master, List<string> format)
        {
            var kind = DiscogsMapper.Classify(format ?? new List<string>(), format ?? new List<string>());
            var album = _albumService.FindById(DiscogsMapper.MasterId(master.Id));

            if (album == null)
            {
                album = DiscogsMapper.MapAlbum(master, kind);
                var trackCount = master.Tracklist.Count(t => t.Type == null || t.Type == "track");
                album.AlbumReleases = new List<AlbumRelease> { DiscogsMapper.MapSearchRelease(DiscogsMapper.ReleaseId(master.MainRelease), trackCount, kind) };
            }

            return WithArtist(album, master.Artists.First());
        }

        private Album MapSearchResult(DiscogsReleaseResource release)
        {
            var album = _albumService.FindById(DiscogsMapper.ReleaseId(release.Id));

            if (album == null)
            {
                album = DiscogsMapper.MapAlbum(release);
                var trackCount = release.Tracklist.Count(t => t.Type == null || t.Type == "track");
                album.AlbumReleases = new List<AlbumRelease> { DiscogsMapper.MapSearchRelease(DiscogsMapper.ReleaseId(release.Id), trackCount, DiscogsMapper.Classify(release.Formats)) };
            }

            return WithArtist(album, release.Artists.First());
        }

        private Album WithArtist(Album album, DiscogsArtistCreditResource credit)
        {
            var artist = _artistService.FindById(DiscogsMapper.ArtistId(credit.Id)) ?? new Artist { Metadata = DiscogsMapper.MapArtist(credit) };

            album.Artist = artist;
            album.ArtistMetadata = artist.Metadata.Value;

            return album;
        }
    }
}
