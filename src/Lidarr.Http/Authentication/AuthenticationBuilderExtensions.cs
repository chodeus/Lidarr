using System;
using System.Linq;
using System.Net.Http;
using System.Net.Security;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Diacritical;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http.Dispatchers;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;

namespace Lidarr.Http.Authentication
{
    public static class AuthenticationBuilderExtensions
    {
        private const string AuthorizedProperty = "lidarr.authorized";

        private static readonly Logger Logger = NzbDroneLogger.GetLogger(typeof(AuthenticationBuilderExtensions));
        private static readonly Regex CookieNameRegex = new Regex(@"[^a-z0-9]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly object BackchannelHttpHandlerLock = new object();

        private static SocketsHttpHandler _backchannelHttpHandler;

        public static AuthenticationBuilder AddApiKey(this AuthenticationBuilder authenticationBuilder, string name, Action<ApiKeyAuthenticationOptions> options)
        {
            return authenticationBuilder.AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(name, options);
        }

        public static AuthenticationBuilder AddNone(this AuthenticationBuilder authenticationBuilder, string name)
        {
            return authenticationBuilder.AddScheme<AuthenticationSchemeOptions, NoAuthenticationHandler>(name, options => { });
        }

        public static AuthenticationBuilder AddExternal(this AuthenticationBuilder authenticationBuilder, string name)
        {
            return authenticationBuilder.AddScheme<AuthenticationSchemeOptions, NoAuthenticationHandler>(name, options => { });
        }

        public static AuthenticationBuilder AddAppAuthentication(this IServiceCollection services)
        {
            services.AddOptions<CookieAuthenticationOptions>(nameof(AuthenticationType.Forms))
                .Configure<IConfigFileProvider>((options, configFileProvider) =>
                {
                    options.Cookie.Name = $"{GetCleanInstanceName(configFileProvider.InstanceName)}Auth";
                    options.AccessDeniedPath = "/login?loginFailed=true";
                    options.LoginPath = "/login";
                    options.ExpireTimeSpan = TimeSpan.FromDays(7);
                    options.SlidingExpiration = true;
                    options.ReturnUrlParameter = "returnUrl";
                });

            return services.AddAuthentication()
                .AddNone(nameof(AuthenticationType.None))
                .AddExternal(nameof(AuthenticationType.External))
                .AddOidc(nameof(AuthenticationType.Oidc))
                .AddCookie(nameof(AuthenticationType.Forms))
                .AddApiKey("API", options =>
                {
                    options.HeaderName = "X-Api-Key";
                    options.QueryName = "apikey";
                })
                .AddApiKey("SignalR", options =>
                {
                    options.HeaderName = "X-Api-Key";
                    options.QueryName = "access_token";
                });
        }

        // The identity provider signs the user into this cookie scheme; the UI policy authenticates against the cookie
        private static AuthenticationBuilder AddOidc(this AuthenticationBuilder authenticationBuilder, string name)
        {
            authenticationBuilder.Services.AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme)
                .Configure<IConfigFileProvider>((options, configFileProvider) =>
                {
                    options.Cookie.Name = $"{GetCleanInstanceName(configFileProvider.InstanceName)}OIDC";
                    options.AccessDeniedPath = "/login?loginFailed=true";
                    options.LoginPath = "/login";
                    options.ReturnUrlParameter = "returnUrl";
                    options.ExpireTimeSpan = TimeSpan.FromDays(7);
                    options.SlidingExpiration = true;
                    options.Cookie.IsEssential = true;
                    options.Events.OnValidatePrincipal = context =>
                    {
                        // A cookie issued before OIDC was switched off or the allowed user changed stops working at once
                        var configuredUser = configFileProvider.OidcUserIdentifier;

                        if (configFileProvider.EffectiveAuthenticationMethod() == AuthenticationType.Oidc &&
                            configuredUser.IsNotNullOrWhiteSpace() &&
                            string.Equals(context.Principal?.FindFirst("user")?.Value, configuredUser, StringComparison.OrdinalIgnoreCase))
                        {
                            return Task.CompletedTask;
                        }

                        context.RejectPrincipal();

                        return context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    };

                    options.Events.OnRedirectToLogin = context => EventOnRedirectCookiesLogin(context, 401);
                    options.Events.OnRedirectToAccessDenied = context => EventOnRedirectCookiesLogin(context, 403);
                });

            authenticationBuilder.Services.AddOptions<OpenIdConnectOptions>(name)
                .Configure<IConfigFileProvider, ICertificateValidationService>((options, configFileProvider, certificateValidationService) =>
                {
                    options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                    options.CorrelationCookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax;
                    options.NonceCookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax;

                    // Secure only over https, as the login cookie: a browser drops a Secure cookie set over plain http, so sign-in never finishes
                    options.CorrelationCookie.SecurePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.SameAsRequest;
                    options.NonceCookie.SecurePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.SameAsRequest;

                    // The scheme is always registered, so it needs valid options even while unused
                    if (configFileProvider.AuthenticationMethod != AuthenticationType.Oidc ||
                        !configFileProvider.IsOidcConfigured())
                    {
                        options.Authority = "https://localhost";
                        options.ClientId = "Lidarr";

                        return;
                    }

                    options.Authority = configFileProvider.OidcAuthority;
                    options.ClientId = configFileProvider.OidcClientId;
                    options.ClientSecret = configFileProvider.OidcClientSecret;
                    options.ResponseType = "code";
                    options.ResponseMode = "query";
                    options.BackchannelHttpHandler = GetBackchannelHttpHandler(certificateValidationService);
                    options.Scope.Clear();

                    foreach (var scope in AuthenticationConfigurationExtensions.GetOidcScopes(configFileProvider.OidcScopes).Distinct(StringComparer.Ordinal))
                    {
                        options.Scope.Add(scope);
                    }

                    options.MapInboundClaims = false;
                    options.GetClaimsFromUserInfoEndpoint = true;
                    options.SaveTokens = true;

                    options.Events.OnTicketReceived = context =>
                    {
                        // Only the ID token is kept, for sign out; access and refresh tokens never reach the cookie
                        var idToken = context.Properties?.GetTokenValue("id_token");

                        context.Properties?.StoreTokens(idToken.IsNullOrWhiteSpace()
                            ? Array.Empty<AuthenticationToken>()
                            : new[] { new AuthenticationToken { Name = "id_token", Value = idToken } });

                        if (context.Properties?.Items.ContainsKey(AuthorizedProperty) == true)
                        {
                            return Task.CompletedTask;
                        }

                        if (!OidcUserAuthorizer.TryAuthorize(configFileProvider.OidcUserIdentifier, context.Principal, null))
                        {
                            context.HandleResponse();
                            context.Response.Redirect(configFileProvider.UrlBase + "/login?ssoFailed=true");
                        }

                        return Task.CompletedTask;
                    };

                    options.Events.OnRedirectToIdentityProviderForSignOut = context =>
                    {
                        if (context.ProtocolMessage.IssuerAddress.IsNullOrWhiteSpace())
                        {
                            Logger.Debug("The OIDC provider doesn't advertise an end session endpoint, signing out locally only");

                            context.HandleResponse();
                            context.Response.Redirect(configFileProvider.UrlBase + "/loggedout");
                        }

                        return Task.CompletedTask;
                    };

                    options.Events.OnRemoteFailure = context =>
                    {
                        Logger.Error(context.Failure, "OIDC authentication failed");

                        context.HandleResponse();
                        context.Response.Redirect(configFileProvider.UrlBase + "/login?ssoFailed=true");

                        return Task.CompletedTask;
                    };

                    options.Events.OnUserInformationReceived = context =>
                    {
                        if (!OidcUserAuthorizer.TryAuthorize(configFileProvider.OidcUserIdentifier, context.Principal, context.User.RootElement))
                        {
                            context.Fail("User is not authorized to access this application");

                            return Task.CompletedTask;
                        }

                        context.Properties.Items[AuthorizedProperty] = "true";

                        return Task.CompletedTask;
                    };
                });

            return authenticationBuilder.AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options => { })
                .AddOpenIdConnect(name, options => { });
        }

        private static SocketsHttpHandler GetBackchannelHttpHandler(ICertificateValidationService certificateValidationService)
        {
            lock (BackchannelHttpHandlerLock)
            {
                return _backchannelHttpHandler ??= new SocketsHttpHandler
                {
                    SslOptions = new SslClientAuthenticationOptions
                    {
                        RemoteCertificateValidationCallback = certificateValidationService.ShouldByPassValidationError
                    }
                };
            }
        }

        // Replace diacritics and non-word characters, which cookie names don't allow
        private static string GetCleanInstanceName(string instanceName)
        {
            return CookieNameRegex.Replace(instanceName.RemoveDiacritics(), string.Empty);
        }

        private static Task EventOnRedirectCookiesLogin(RedirectContext<CookieAuthenticationOptions> context, int statusCode)
        {
            if (string.Equals(context.Request.Query[HeaderNames.XRequestedWith], "XMLHttpRequest", StringComparison.Ordinal) ||
                string.Equals(context.Request.Headers.XRequestedWith, "XMLHttpRequest", StringComparison.Ordinal))
            {
                context.Response.Headers.Location = context.RedirectUri;
                context.Response.StatusCode = statusCode;
            }
            else
            {
                context.Response.Redirect(context.RedirectUri);
            }

            return Task.CompletedTask;
        }
    }
}
