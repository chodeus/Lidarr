using System;
using System.Collections.Generic;
using FluentValidation;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.MetadataSource.SkyHook;
using NzbDrone.Core.Music;
using NzbDrone.Core.Profiles.Metadata;
using NzbDrone.Core.ThingiProvider;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.MetadataSource.Sources.SkyHookServer
{
    public class SkyHookServerMetadataSourceSettingsValidator : AbstractValidator<SkyHookServerMetadataSourceSettings>
    {
        public SkyHookServerMetadataSourceSettingsValidator()
        {
            RuleFor(c => c.Url).NotEmpty().IsValidUrl();
        }
    }

    public class SkyHookServerMetadataSourceSettings : IProviderConfig
    {
        private static readonly SkyHookServerMetadataSourceSettingsValidator Validator = new();

        [FieldDefinition(0, Label = "URL", Type = FieldType.Url, HelpText = "Address of a server that answers like Lidarr's metadata server, including the API version, such as https://metadata.example.com/api/v0.4")]
        public string Url { get; set; } = string.Empty;

        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }

    // Any server that speaks Lidarr's metadata API, such as a MusicBrainz mirror
    public class SkyHookServerMetadataSource : MetadataSourceBase<SkyHookServerMetadataSourceSettings>
    {
        private readonly IHttpClient _httpClient;
        private readonly IArtistService _artistService;
        private readonly IAlbumService _albumService;
        private readonly Logger _logger;
        private readonly IMetadataProfileService _metadataProfileService;
        private readonly ICacheManager _cacheManager;

        private SkyHookProxy _skyHook;
        private string _skyHookUrl;

        public SkyHookServerMetadataSource(IHttpClient httpClient,
                                           IArtistService artistService,
                                           IAlbumService albumService,
                                           Logger logger,
                                           IMetadataProfileService metadataProfileService,
                                           ICacheManager cacheManager)
        {
            _httpClient = httpClient;
            _artistService = artistService;
            _albumService = albumService;
            _logger = logger;
            _metadataProfileService = metadataProfileService;
            _cacheManager = cacheManager;
        }

        public override string Name => "SkyHook-compatible server";

        public override string IdFamily => MetadataIdFamilies.MusicBrainz;

        public override bool CanHandleId(string foreignId) => MusicBrainzIds.IsMusicBrainzId(foreignId);

        public override ValidationResult Test()
        {
            try
            {
                SkyHook.SearchForNewArtist("test");
                return new ValidationResult();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Unable to reach the metadata server");
                return new ValidationResult(new[] { new ValidationFailure(nameof(SkyHookServerMetadataSourceSettings.Url), "Unable to reach the metadata server: " + ex.Message) });
            }
        }

        public override Artist GetArtistInfo(string foreignArtistId, int metadataProfileId) => SkyHook.GetArtistInfo(foreignArtistId, metadataProfileId);

        public override HashSet<string> GetChangedArtists(DateTime startTime) => SkyHook.GetChangedArtists(startTime);

        public override Tuple<string, Album, List<ArtistMetadata>> GetAlbumInfo(string foreignAlbumId) => SkyHook.GetAlbumInfo(foreignAlbumId);

        public override HashSet<string> GetChangedAlbums(DateTime startTime) => SkyHook.GetChangedAlbums(startTime);

        public override List<Artist> SearchForNewArtist(string title) => SkyHook.SearchForNewArtist(title);

        public override List<Album> SearchForNewAlbum(string title, string artist) => SkyHook.SearchForNewAlbum(title, artist);

        public override List<Album> SearchForNewAlbumByRecordingIds(List<string> recordingIds) => SkyHook.SearchForNewAlbumByRecordingIds(recordingIds);

        public override List<object> SearchForNewEntity(string title) => SkyHook.SearchForNewEntity(title);

        private SkyHookProxy SkyHook
        {
            get
            {
                if (_skyHook == null || _skyHookUrl != Settings.Url)
                {
                    _skyHookUrl = Settings.Url;
                    _skyHook = new SkyHookProxy(_httpClient, new ServerRequestBuilder(_skyHookUrl), _artistService, _albumService, _logger, _metadataProfileService, _cacheManager);
                }

                return _skyHook;
            }
        }

        private class ServerRequestBuilder : IMetadataRequestBuilder
        {
            private readonly IHttpRequestBuilderFactory _factory;

            public ServerRequestBuilder(string url)
            {
                _factory = new HttpRequestBuilder(url.TrimEnd("/") + "/{route}").KeepAlive().CreateFactory();
            }

            public IHttpRequestBuilderFactory GetRequestBuilder() => _factory;
        }
    }
}
