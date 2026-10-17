using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DryIoc;
using DryIoc.Microsoft.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Composition;
using NzbDrone.Common.Composition.Extensions;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Common.Options;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Extensions;
using NzbDrone.Core.MediaFiles.TrackImport;
using NzbDrone.Core.MediaFiles.TrackImport.Specifications;
using NzbDrone.Core.Parser.Model;
using NzbDrone.SignalR;
using NzbDrone.Test.Common;

namespace NzbDrone.App.Test
{
    // A file of its own, so it doesn't collide with other branches' checks in ContainerFixture
    [TestFixture]
    public class MoreTracksSpecificationContainerFixture : TestBase
    {
        [Test]
        public void should_build_the_spec_with_its_full_constructor()
        {
            var container = new Container(rules => rules.WithNzbDroneRules())
                .AutoAddServices(AssemblyLoader.LoadBaseAssemblies())
                .AddNzbDroneLogger()
                .AddDummyDatabase()
                .AddStartupContext(new StartupContext("first", "second"));

            container.RegisterInstance<IHostLifetime>(new Mock<IHostLifetime>().Object);
            container.RegisterInstance<IBroadcastSignalRMessage>(new Mock<IBroadcastSignalRMessage>().Object);
            container.RegisterInstance<IOptions<PostgresOptions>>(new Mock<IOptions<PostgresOptions>>().Object);
            container.RegisterInstance<IOptions<AuthOptions>>(new Mock<IOptions<AuthOptions>>().Object);
            container.RegisterInstance<IOptions<AppOptions>>(new Mock<IOptions<AppOptions>>().Object);
            container.RegisterInstance<IOptions<ServerOptions>>(new Mock<IOptions<ServerOptions>>().Object);
            container.RegisterInstance<IOptions<UpdateOptions>>(new Mock<IOptions<UpdateOptions>>().Object);
            container.RegisterInstance<IOptions<LogOptions>>(new Mock<IOptions<LogOptions>>().Object);

            var spec = container.GetServiceProvider()
                                .GetRequiredService<IEnumerable<IImportDecisionEngineSpecification<LocalAlbumRelease>>>()
                                .OfType<MoreTracksSpecification>()
                                .Single();

            typeof(MoreTracksSpecification).GetField("_upgradableSpecification", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(spec).Should().NotBeNull();
        }
    }
}
