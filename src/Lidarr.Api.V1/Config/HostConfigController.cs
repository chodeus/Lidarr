using System;
using System.IO;
using System.Linq;
using System.Reflection;
using FluentValidation;
using Lidarr.Http;
using Lidarr.Http.REST;
using Lidarr.Http.REST.Attributes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Network;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Update;
using NzbDrone.Core.Validation;
using NzbDrone.Core.Validation.Paths;

namespace Lidarr.Api.V1.Config
{
    [V1ApiController("config/host")]
    public class HostConfigController : RestController<HostConfigResource>
    {
        private const string PrivateValue = "********";

        private readonly IConfigFileProvider _configFileProvider;
        private readonly IConfigService _configService;
        private readonly IUserService _userService;

        public HostConfigController(IConfigFileProvider configFileProvider,
                                    IConfigService configService,
                                    IUserService userService,
                                    FileExistsValidator fileExistsValidator)
            : this(configFileProvider, configService, userService, fileExistsValidator, null)
        {
        }

        [ActivatorUtilitiesConstructor]
        public HostConfigController(IConfigFileProvider configFileProvider,
                                    IConfigService configService,
                                    IUserService userService,
                                    FileExistsValidator fileExistsValidator,
                                    OidcAuthorityValidator oidcAuthorityValidator)
        {
            _configFileProvider = configFileProvider;
            _configService = configService;
            _userService = userService;

            SharedValidator.RuleFor(c => c.BindAddress)
                           .ValidIpAddress()
                           .When(c => c.BindAddress != "*" && c.BindAddress != "localhost");

            SharedValidator.RuleFor(c => c.Port).ValidPort();

            SharedValidator.RuleFor(c => c.AllowedHosts).NotNull();

            SharedValidator.RuleFor(c => c.AllowedHosts)
                           .Must(h => AllowedHostsParser.Parse(h).Any())
                           .When(c => c.AuthenticationRequired != AuthenticationRequiredType.Enabled)
                           .WithMessage("Allowed Hosts is required when 'Authentication Required' is not 'Enabled'");

            SharedValidator.RuleFor(c => c.AllowedHosts)
                           .ValidHosts()
                           .When(c => c.AllowedHosts.IsNotNullOrWhiteSpace());

            SharedValidator.RuleFor(c => c.UrlBase).ValidUrlBase();
            SharedValidator.RuleFor(c => c.TrustedNetworks).ValidIpNetworks();
            SharedValidator.RuleFor(c => c.InstanceName).ContainsLidarr().When(c => c.InstanceName.IsNotNullOrWhiteSpace());

            SharedValidator.RuleFor(c => c.Username).NotEmpty().When(c => c.AuthenticationMethod == AuthenticationType.Forms);
            SharedValidator.RuleFor(c => c.Password).NotEmpty().When(c => c.AuthenticationMethod == AuthenticationType.Forms);

            SharedValidator.RuleFor(c => c.AuthenticationMethod)
#pragma warning disable CS0618 // Type or member is obsolete
                .NotEqual(AuthenticationType.Basic)
#pragma warning restore CS0618 // Type or member is obsolete
                .WithMessage("'Basic' is no longer supported, switch to 'Forms' instead.");

            SharedValidator.RuleFor(c => c.OidcAuthority)
                .Cascade(CascadeMode.Stop)
                .NotEmpty()
                .Must(c => c is not null && c.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                .WithMessage("OIDC Authority must start with 'https://'")
                .When(c => c.AuthenticationMethod == AuthenticationType.Oidc)
                .WithName("OIDC Authority");

            // Only a changed Authority is fetched, so saving other settings doesn't depend on the provider being up
            if (oidcAuthorityValidator != null)
            {
                SharedValidator.RuleFor(c => c.OidcAuthority)
                    .SetValidator(oidcAuthorityValidator)
                    .When(c => c.AuthenticationMethod == AuthenticationType.Oidc &&
                               c.OidcAuthority != _configFileProvider.OidcAuthority)
                    .WithName("OIDC Authority");
            }

            SharedValidator.RuleFor(c => c.OidcClientId).NotEmpty()
                .When(c => c.AuthenticationMethod == AuthenticationType.Oidc)
                .WithName("OIDC Client ID");

            SharedValidator.RuleFor(c => c.OidcClientSecret).NotEmpty()
                .When(c => c.AuthenticationMethod == AuthenticationType.Oidc)
                .WithName("OIDC Client Secret");

            // The stored secret only goes to the Authority it was saved with, whichever method is selected
            SharedValidator.RuleFor(c => c.OidcClientSecret)
                .NotEqual(PrivateValue)
                .When(c => c.OidcAuthority != _configFileProvider.OidcAuthority)
                .WithMessage("Enter the OIDC Client Secret again after changing the OIDC Authority");

            SharedValidator.RuleFor(c => c.OidcUserIdentifier).NotEmpty()
                .When(c => c.AuthenticationMethod == AuthenticationType.Oidc)
                .WithName("OIDC User");

            SharedValidator.RuleFor(c => c.OidcScopes)
                .Cascade(CascadeMode.Stop)
                .NotEmpty()
                .Must(c => Array.IndexOf(AuthenticationConfigurationExtensions.GetOidcScopes(c), "openid") >= 0)
                .WithMessage("OIDC Scopes must include 'openid'")
                .When(c => c.AuthenticationMethod == AuthenticationType.Oidc)
                .WithName("OIDC Scopes");

            SharedValidator.RuleFor(c => c.PasswordConfirmation)
                .Must((resource, p) => IsMatchingPassword(resource)).WithMessage("Must match Password");

            SharedValidator.RuleFor(c => c.SslPort).ValidPort().When(c => c.EnableSsl);
            SharedValidator.RuleFor(c => c.SslPort).NotEqual(c => c.Port).When(c => c.EnableSsl);

            SharedValidator.RuleFor(c => c.SslCertPath)
                .Cascade(CascadeMode.Stop)
                .NotEmpty()
                .IsValidPath()
                .SetValidator(fileExistsValidator)
                .IsValidCertificate()
                .When(c => c.EnableSsl);

            SharedValidator.RuleFor(c => c.LogSizeLimit).InclusiveBetween(1, 10);

            SharedValidator.RuleFor(c => c.Branch).NotEmpty().WithMessage("Branch name is required, 'master' is the default");
            SharedValidator.RuleFor(c => c.UpdateScriptPath).IsValidPath().When(c => c.UpdateMechanism == UpdateMechanism.Script);

            SharedValidator.RuleFor(c => c.BackupFolder).IsValidPath().When(c => Path.IsPathRooted(c.BackupFolder));
            SharedValidator.RuleFor(c => c.BackupInterval).InclusiveBetween(1, 7);
            SharedValidator.RuleFor(c => c.BackupRetention).InclusiveBetween(1, 90);
        }

        private bool IsMatchingPassword(HostConfigResource resource)
        {
            var user = _userService.FindUser();

            if (user != null && user.Password == resource.Password)
            {
                return true;
            }

            if (resource.Password == resource.PasswordConfirmation)
            {
                return true;
            }

            return false;
        }

        public override HostConfigResource GetResourceById(int id)
        {
            return GetHostConfig();
        }

        [HttpGet]
        public HostConfigResource GetHostConfig()
        {
            var resource = _configFileProvider.ToResource(_configService);
            resource.Id = 1;

            var user = _userService.FindUser();

            resource.Username = user?.Username ?? string.Empty;
            resource.Password = user?.Password ?? string.Empty;
            resource.PasswordConfirmation = string.Empty;

            // The client secret never leaves the server; a placeholder says one is set
            resource.OidcClientSecret = _configFileProvider.OidcClientSecret.IsNullOrWhiteSpace() ? string.Empty : PrivateValue;

            return resource;
        }

        [RestPutById]
        public ActionResult<HostConfigResource> SaveHostConfig([FromBody] HostConfigResource resource)
        {
            resource.TrustedNetworks = IPNetworkParser.NormalizeList(resource.TrustedNetworks);

            var dictionary = resource.GetType()
                                     .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                                     .ToDictionary(prop => prop.Name, prop => prop.GetValue(resource, null));

            // The placeholder sent back unchanged keeps the stored secret
            if (resource.OidcClientSecret == PrivateValue)
            {
                dictionary["OidcClientSecret"] = _configFileProvider.OidcClientSecret;
            }

            _configFileProvider.SaveConfigDictionary(dictionary);
            _configService.SaveConfigDictionary(dictionary);

            if (resource.Username.IsNotNullOrWhiteSpace() && resource.Password.IsNotNullOrWhiteSpace())
            {
                _userService.Upsert(resource.Username, resource.Password);
            }

            return Accepted(resource.Id);
        }
    }
}
