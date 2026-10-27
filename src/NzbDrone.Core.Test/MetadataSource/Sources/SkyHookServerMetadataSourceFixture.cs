using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using FluentAssertions;
using Moq;
using NLog;
using NLog.Config;
using NLog.Targets;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Http;
using NzbDrone.Core.MetadataSource.SkyHook;
using NzbDrone.Core.MetadataSource.SkyHook.Resource;
using NzbDrone.Core.MetadataSource.Sources;
using NzbDrone.Core.MetadataSource.Sources.SkyHookServer;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MetadataSource.Sources
{
    [TestFixture]
    public class SkyHookServerMetadataSourceFixture : CoreTest<SkyHookServerMetadataSource>
    {
        [Test]
        public void changed_albums_come_from_its_own_server_not_another_lookups_cache()
        {
            var shared = new CacheManager();
            shared.GetCache<HashSet<string>>(typeof(SkyHookProxy)).Set("ChangedAlbums", new HashSet<string> { "from-another-server" });
            Mocker.SetConstant<ICacheManager>(shared);

            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Get<RecentUpdatesResource>(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(r => new HttpResponse<RecentUpdatesResource>(new HttpResponse(r, new HttpHeader(), "{\"limited\":false,\"items\":[\"from-this-server\"]}")));

            Subject.Definition = new MetadataSourceDefinition
            {
                Settings = new SkyHookServerMetadataSourceSettings { Url = "https://metadata.example.invalid/api/v0.4" }
            };

            Subject.GetChangedAlbums(DateTime.UtcNow).Should().BeEquivalentTo(new[] { "from-this-server" });
        }

        [Test]
        public void test_should_report_an_unreachable_server_without_its_url()
        {
            const string token = "s3cr3ttoken";
            Mocker.SetConstant<ICacheManager>(new CacheManager());

            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Get<List<ArtistResource>>(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(r => throw new HttpException(r, new HttpResponse(r, new HttpHeader(), string.Empty, HttpStatusCode.InternalServerError)));

            Subject.Definition = new MetadataSourceDefinition
            {
                Name = "Mirror",
                Settings = new SkyHookServerMetadataSourceSettings { Url = "https://metadata.example.invalid/" + token + "/api/v0.4" }
            };

            var target = new MemoryTarget("skyhook-server-test") { Layout = "${message}" };
            var config = LogManager.Configuration ?? new LoggingConfiguration();
            config.AddRuleForAllLevels(target);
            LogManager.Configuration = config;

            try
            {
                Subject.Test().Errors.Single().ErrorMessage.Should().Contain("HTTP 500").And.NotContain(token);

                target.Logs.Should().ContainSingle(l => l.StartsWith("Unable to reach the metadata server")).Which.Should().Contain("Mirror").And.NotContain(token);
            }
            finally
            {
                config.RemoveTarget(target.Name);
                LogManager.Configuration = config;
            }

            // SkyHookProxy also warns about the failed request, with its URL; see the README on the log cleanser
            ExceptionVerification.ExpectedWarns(2);
        }
    }
}
