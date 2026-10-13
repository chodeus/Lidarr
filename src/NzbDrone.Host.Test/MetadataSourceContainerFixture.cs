using System;
using System.Collections.Generic;
using System.Reflection;
using DryIoc;
using DryIoc.Microsoft.DependencyInjection;
using FluentAssertions;
using Lidarr.Api.V1.Search;
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
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.Sources;
using NzbDrone.Core.Music;
using NzbDrone.Core.Organizer;
using NzbDrone.SignalR;
using NzbDrone.Test.Common;
using IServiceProvider = System.IServiceProvider;

namespace NzbDrone.App.Test
{
    [TestFixture]
    public class MetadataSourceContainerFixture : TestBase
    {
        private IServiceProvider _container;

        [SetUp]
        public void SetUp()
        {
            var container = new Container(rules => rules.WithNzbDroneRules())
                .AutoAddServices(AssemblyLoader.LoadBaseAssemblies())
                .AddNzbDroneLogger()
                .AddDummyDatabase()
                .AddStartupContext(new StartupContext("first", "second"));

            // A plugin's own lookup registers after Lidarr's, like Sleezer's does on standard Lidarr
            container.Register<IProvideArtistInfo, PluginArtistLookup>(Reuse.Singleton);

            container.AddMetadataSources();

            container.Register<SearchController>(Reuse.Transient);
            container.RegisterInstance(new Mock<IBuildFileNames>().Object, IfAlreadyRegistered.Replace);
            container.RegisterInstance(new Mock<IMapCoversToLocal>().Object, IfAlreadyRegistered.Replace);
            container.RegisterInstance<IHostLifetime>(new Mock<IHostLifetime>().Object);
            container.RegisterInstance<IBroadcastSignalRMessage>(new Mock<IBroadcastSignalRMessage>().Object);
            container.RegisterInstance<IOptions<PostgresOptions>>(new Mock<IOptions<PostgresOptions>>().Object);
            container.RegisterInstance<IOptions<AppOptions>>(new Mock<IOptions<AppOptions>>().Object);
            container.RegisterInstance<IOptions<LogOptions>>(new Mock<IOptions<LogOptions>>().Object);

            _container = container.GetServiceProvider();
        }

        [Test]
        public void lookups_go_through_the_metadata_source_profiles_even_when_a_plugin_registers_its_own()
        {
            _container.GetRequiredService<IProvideArtistInfo>().Should().BeOfType<MetadataSourceAggregator>();
            _container.GetRequiredService<IProvideAlbumInfo>().Should().BeOfType<MetadataSourceAggregator>();
        }

        [Test]
        public void no_other_lookup_stays_registered()
        {
            _container.GetServices<IProvideArtistInfo>().Should().ContainSingle();
            _container.GetServices<ISearchForNewEntity>().Should().ContainSingle();
        }

        [Test]
        public void search_labels_come_from_the_instance_that_searched()
        {
            _container.GetRequiredService<IMetadataSourceLabels>().Should().BeSameAs(_container.GetRequiredService<ISearchForNewEntity>());
        }

        [Test]
        public void search_controller_gets_the_source_labels()
        {
            var controller = _container.GetRequiredService<SearchController>();

            typeof(SearchController).GetField("_labels", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller).Should().NotBeNull();
        }

        private class PluginArtistLookup : IProvideArtistInfo
        {
            public Artist GetArtistInfo(string lidarrId, int metadataProfileId) => throw new NotImplementedException();

            public HashSet<string> GetChangedArtists(DateTime startTime) => throw new NotImplementedException();
        }
    }
}
