using System;
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

            // The request route is appended to the URL, so a query string would swallow it; put tokens in the path
            RuleFor(c => c.Url).Must(HasNoQueryOrUserInfo).WithMessage("Must not include a query string or a user name and password");
        }

        private static bool HasNoQueryOrUserInfo(string url)
        {
            return !Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Query.IsNullOrWhiteSpace() && uri.UserInfo.IsNullOrWhiteSpace());
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
    public class SkyHookServerMetadataSource : SkyHookMetadataSourceBase<SkyHookServerMetadataSourceSettings>
    {
        private readonly IHttpClient _httpClient;
        private readonly IArtistService _artistService;
        private readonly IAlbumService _albumService;
        private readonly Logger _logger;
        private readonly IMetadataProfileService _metadataProfileService;

        private SkyHookProxy _skyHook;
        private string _skyHookUrl;

        public SkyHookServerMetadataSource(IHttpClient httpClient,
                                           IArtistService artistService,
                                           IAlbumService albumService,
                                           Logger logger,
                                           IMetadataProfileService metadataProfileService)
        {
            _httpClient = httpClient;
            _artistService = artistService;
            _albumService = albumService;
            _logger = logger;
            _metadataProfileService = metadataProfileService;
        }

        public override string Name => "SkyHook-compatible server";

        public override ValidationResult Test()
        {
            try
            {
                SkyHook.SearchForNewArtist("test");
                return new ValidationResult();
            }
            catch (Exception ex)
            {
                var reason = MetadataSourceErrors.Describe(ex);

                _logger.Warn("Unable to reach the metadata server: {0}", reason);
                return new ValidationResult(new[] { new ValidationFailure(nameof(SkyHookServerMetadataSourceSettings.Url), "Unable to reach the metadata server: " + reason) });
            }
        }

        // Own cache: SkyHookProxy keys ChangedAlbums by type, so a shared one would answer with another server's list
        protected override SkyHookProxy SkyHook
        {
            get
            {
                if (_skyHook == null || _skyHookUrl != Settings.Url)
                {
                    _skyHookUrl = Settings.Url;
                    _skyHook = new SkyHookProxy(_httpClient, new ServerRequestBuilder<SkyHookServerMetadataSource>(_skyHookUrl), _artistService, _albumService, _logger, _metadataProfileService, new CacheManager());
                }

                return _skyHook;
            }
        }

        // Generic so the startup scan, which registers every IMetadataRequestBuilder it finds, can't pick it up
        private class ServerRequestBuilder<TOwner> : IMetadataRequestBuilder
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
