using System;
using System.Collections.Generic;
using System.Linq;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Music;
using NzbDrone.Core.Profiles.Metadata;
using NzbDrone.Core.ThingiProvider;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.MetadataSource.Sources.Deezer
{
    public class DeezerMetadataSourceSettings : IProviderConfig
    {
        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult();
        }
    }

    public class DeezerMetadataSource : MetadataSourceBase<DeezerMetadataSourceSettings>
    {
        private const int SearchLimit = 10;

        private readonly IDeezerApi _api;
        private readonly IArtistService _artistService;
        private readonly IAlbumService _albumService;
        private readonly IMetadataProfileService _metadataProfileService;
        private readonly Logger _logger;

        public DeezerMetadataSource(IDeezerApi api,
                                    IArtistService artistService,
                                    IAlbumService albumService,
                                    IMetadataProfileService metadataProfileService,
                                    Logger logger)
        {
            _api = api;
            _artistService = artistService;
            _albumService = albumService;
            _metadataProfileService = metadataProfileService;
            _logger = logger;
        }

        public override string Name => "Deezer";

        public override string IdFamily => MetadataIdFamilies.Deezer;

        public override bool CanHandleId(string foreignId) => DeezerMapper.TryParseForeignId(foreignId, out _);

        public override ValidationResult Test()
        {
            try
            {
                _api.SearchArtists("test", 1);
                return new ValidationResult();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Unable to reach Deezer");
                return new ValidationResult(new[] { new ValidationFailure(string.Empty, "Unable to reach Deezer: " + ex.Message) });
            }
        }

        public override Artist GetArtistInfo(string foreignArtistId, int metadataProfileId)
        {
            var id = ParseId(foreignArtistId);
            var resource = _api.GetArtist(id) ?? throw new ArtistNotFoundException(foreignArtistId);
            var filter = MetadataProfileAlbumFilter.For(_metadataProfileService, metadataProfileId);

            var artist = new Artist { Metadata = DeezerMapper.MapArtist(resource) };
            artist.CleanName = Parser.Parser.CleanArtistName(artist.Metadata.Value.Name);
            artist.SortName = Parser.Parser.NormalizeTitle(artist.Metadata.Value.Name);
            artist.Albums = (_api.GetArtistAlbums(id) ?? new List<DeezerAlbumResource>())
                .Select(DeezerMapper.MapAlbum)
                .Where(album => filter.Allows(album.AlbumType, album.SecondaryTypes.Select(t => t.Name).ToList(), DeezerMapper.ReleaseStatuses))
                .ToList();

            return artist;
        }

        public override Tuple<string, Album, List<ArtistMetadata>> GetAlbumInfo(string foreignAlbumId)
        {
            var id = ParseId(foreignAlbumId);
            var resource = _api.GetAlbum(id) ?? throw new AlbumNotFoundException(foreignAlbumId);
            var tracks = _api.GetAlbumTracks(id) ?? new List<DeezerTrackResource>();

            var artists = tracks.Select(t => t.Artist)
                .Prepend(resource.Artist)
                .DistinctBy(a => a.Id)
                .Select(DeezerMapper.MapArtist)
                .ToDictionary(a => a.ForeignArtistId);

            var album = DeezerMapper.MapAlbum(resource);
            album.AlbumReleases = new List<AlbumRelease> { DeezerMapper.MapRelease(resource, tracks, artists) };

            var artistId = DeezerMapper.ToForeignId(resource.Artist.Id);
            album.ArtistMetadata = artists[artistId];

            return Tuple.Create(artistId, album, artists.Values.ToList());
        }

        public override List<Artist> SearchForNewArtist(string title)
        {
            var resources = DeezerMapper.TryParseForeignId(title?.Trim(), out var id)
                ? new[] { _api.GetArtist(id) }.Where(a => a != null).ToList()
                : _api.SearchArtists(title, SearchLimit);

            return resources.Select(MapSearchResult).ToList();
        }

        public override List<Album> SearchForNewAlbum(string title, string artist)
        {
            // Search results lack the release date, so each is read in full
            var resources = DeezerMapper.TryParseForeignId(title?.Trim(), out var id)
                ? new List<long> { id }
                : _api.SearchAlbums(AlbumQuery(title, artist), SearchLimit).Select(a => a.Id).ToList();

            return resources.Select(_api.GetAlbum)
                .Where(a => a != null)
                .Select(MapSearchResult)
                .ToList();
        }

        public override List<object> SearchForNewEntity(string title)
        {
            if (DeezerMapper.TryParseForeignId(title?.Trim(), out _))
            {
                // An ID can name an artist or an album, so the artist is tried first
                var artist = SearchForNewArtist(title);

                return artist.Any() ? artist.Cast<object>().ToList() : SearchForNewAlbum(title, null).Cast<object>().ToList();
            }

            return SearchForNewArtist(title).Cast<object>()
                .Concat(SearchForNewAlbum(title, null))
                .ToList();
        }

        private static long ParseId(string foreignId)
        {
            return DeezerMapper.TryParseForeignId(foreignId, out var id) ? id : throw new ArgumentException("Not a Deezer ID: " + foreignId);
        }

        // Deezer's advanced search matches each field separately
        private static string AlbumQuery(string title, string artist)
        {
            return artist.IsNullOrWhiteSpace() ? title : $"artist:\"{artist.Replace("\"", "")}\" album:\"{title.Replace("\"", "")}\"";
        }

        private Artist MapSearchResult(DeezerArtistResource resource)
        {
            return _artistService.FindById(DeezerMapper.ToForeignId(resource.Id)) ?? new Artist { Metadata = DeezerMapper.MapArtist(resource) };
        }

        private Album MapSearchResult(DeezerAlbumResource resource)
        {
            var artist = _artistService.FindById(DeezerMapper.ToForeignId(resource.Artist.Id)) ?? new Artist { Metadata = DeezerMapper.MapArtist(resource.Artist) };

            var album = _albumService.FindById(DeezerMapper.ToForeignId(resource.Id));

            if (album == null)
            {
                album = DeezerMapper.MapAlbum(resource);
                album.AlbumReleases = new List<AlbumRelease> { DeezerMapper.MapSearchRelease(resource) };
            }

            album.Artist = artist;
            album.ArtistMetadata = artist.Metadata.Value;

            return album;
        }
    }
}
