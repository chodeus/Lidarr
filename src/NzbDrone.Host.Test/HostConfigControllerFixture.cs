using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DryIoc;
using DryIoc.Microsoft.DependencyInjection;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using FluentValidation.Validators;
using Lidarr.Api.V1.Config;
using Lidarr.Http.REST;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Composition;
using NzbDrone.Common.Composition.Extensions;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Common.Options;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Configuration.Events;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Extensions;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Validation;
using NzbDrone.Core.Validation.Paths;
using NzbDrone.SignalR;
using NzbDrone.Test.Common;

namespace NzbDrone.App.Test
{
    [TestFixture]
    public class HostConfigControllerFixture : TestBase
    {
        private static IValidator<HostConfigResource> GetSharedValidator(HostConfigController controller)
        {
            return (IValidator<HostConfigResource>)typeof(RestController<HostConfigResource>)
                .GetProperty("SharedValidator", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(controller);
        }

        private static IEnumerable<IPropertyValidator> GetAuthorityValidators(HostConfigController controller)
        {
            return GetSharedValidator(controller).CreateDescriptor().GetValidatorsForMember(nameof(HostConfigResource.OidcAuthority));
        }

        private static IList<ValidationFailure> ValidateSecret(string authority, string secret, AuthenticationType method)
        {
            var config = new Mock<IConfigFileProvider>();
            config.SetupGet(c => c.OidcAuthority).Returns("https://auth.example.com/old/");

            var controller = new HostConfigController(config.Object,
                                                      Mock.Of<IConfigService>(),
                                                      Mock.Of<IUserService>(),
                                                      new FileExistsValidator(Mock.Of<IDiskProvider>()));

            var resource = new HostConfigResource { AuthenticationMethod = method, OidcAuthority = authority, OidcClientSecret = secret };

            return GetSharedValidator(controller).Validate(resource, o => o.IncludeProperties(nameof(HostConfigResource.OidcClientSecret))).Errors;
        }

        [Test]
        public void container_should_build_it_with_the_oidc_authority_check()
        {
            var container = new Container(rules => rules.WithNzbDroneRules())
                .AutoAddServices(AssemblyLoader.LoadBaseAssemblies())
                .AddNzbDroneLogger()
                .AddDummyDatabase()
                .AddStartupContext(new StartupContext("first", "second"));

            container.Register<HostConfigController>(Reuse.Transient);
            container.RegisterInstance<IHostLifetime>(new Mock<IHostLifetime>().Object);
            container.RegisterInstance<IBroadcastSignalRMessage>(new Mock<IBroadcastSignalRMessage>().Object);
            container.RegisterInstance<IOptions<PostgresOptions>>(new Mock<IOptions<PostgresOptions>>().Object);
            container.RegisterInstance<IOptions<AuthOptions>>(new Mock<IOptions<AuthOptions>>().Object);
            container.RegisterInstance<IOptions<AppOptions>>(new Mock<IOptions<AppOptions>>().Object);
            container.RegisterInstance<IOptions<ServerOptions>>(new Mock<IOptions<ServerOptions>>().Object);
            container.RegisterInstance<IOptions<UpdateOptions>>(new Mock<IOptions<UpdateOptions>>().Object);
            container.RegisterInstance<IOptions<LogOptions>>(new Mock<IOptions<LogOptions>>().Object);

            var controller = container.GetServiceProvider().GetRequiredService<HostConfigController>();

            GetAuthorityValidators(controller).Should().Contain(v => v is OidcAuthorityValidator);
        }

        [Test]
        public void develop_constructor_should_still_build_it()
        {
            var controller = new HostConfigController(Mock.Of<IConfigFileProvider>(),
                                                      Mock.Of<IConfigService>(),
                                                      Mock.Of<IUserService>(),
                                                      new FileExistsValidator(Mock.Of<IDiskProvider>()));

            GetAuthorityValidators(controller).Should().NotContain(v => v is OidcAuthorityValidator);
        }

        [TestCase(AuthenticationType.Oidc)]
        [TestCase(AuthenticationType.Forms)]
        public void should_ask_for_the_secret_again_after_the_authority_changes(AuthenticationType method)
        {
            ValidateSecret("https://auth.example.com/new/", "********", method)
                .Should().ContainSingle(e => e.PropertyName == nameof(HostConfigResource.OidcClientSecret));
        }

        [Test]
        public void should_keep_the_stored_secret_while_the_authority_is_unchanged()
        {
            ValidateSecret("https://auth.example.com/old/", "********", AuthenticationType.Oidc).Should().BeEmpty();
        }

        [Test]
        public void should_take_a_new_secret_with_a_new_authority()
        {
            ValidateSecret("https://auth.example.com/new/", "new-client-secret", AuthenticationType.Oidc).Should().BeEmpty();
        }

        [Test]
        public void should_keep_the_stored_oidc_settings_when_a_save_leaves_them_out()
        {
            var files = new Dictionary<string, string>();
            var disk = new Mock<IDiskProvider>();
            disk.Setup(d => d.FileExists(It.IsAny<string>())).Returns<string>(files.ContainsKey);
            disk.Setup(d => d.ReadAllText(It.IsAny<string>())).Returns<string>(p => files[p]);
            disk.Setup(d => d.WriteAllText(It.IsAny<string>(), It.IsAny<string>())).Callback<string, string>((p, c) => files[p] = c);

            var appFolder = new Mock<IAppFolderInfo>();
            appFolder.SetupGet(a => a.AppDataFolder).Returns(Path.Combine(Path.GetTempPath(), "host-config"));

            var events = new Mock<IEventAggregator>();
            var configFileProvider = new ConfigFileProvider(appFolder.Object,
                                                            new CacheManager(),
                                                            events.Object,
                                                            disk.Object,
                                                            Options.Create(new PostgresOptions()),
                                                            Options.Create(new AuthOptions()),
                                                            Options.Create(new AppOptions()),
                                                            Options.Create(new ServerOptions()),
                                                            Options.Create(new UpdateOptions()),
                                                            Options.Create(new LogOptions()));

            var controller = new HostConfigController(configFileProvider,
                                                      Mock.Of<IConfigService>(),
                                                      Mock.Of<IUserService>(),
                                                      new FileExistsValidator(Mock.Of<IDiskProvider>()));

            var resource = controller.GetHostConfig();
            resource.OidcClientId = "lidarr";
            controller.SaveHostConfig(resource);
            events.Invocations.Clear();

            // A client built before the OIDC settings doesn't send them
            resource = controller.GetHostConfig();
            resource.LogLevel = "debug";
            resource.OidcAuthority = null;
            resource.OidcClientId = null;
            resource.OidcClientSecret = null;
            resource.OidcUserIdentifier = null;
            resource.OidcScopes = null;
            controller.SaveHostConfig(resource);

            configFileProvider.LogLevel.Should().Be("debug");
            configFileProvider.OidcClientId.Should().Be("lidarr");
            events.Verify(e => e.PublishEvent(It.IsAny<ConfigFileSavedEvent>()), Times.Once());
        }
    }
}
