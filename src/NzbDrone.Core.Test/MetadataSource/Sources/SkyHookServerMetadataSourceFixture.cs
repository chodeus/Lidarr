using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using FluentAssertions;
using Moq;
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
                Settings = new SkyHookServerMetadataSourceSettings { Url = "https://metadata.example.invalid/" + token + "/api/v0.4" }
            };

            Subject.Test().Errors.Single().ErrorMessage.Should().Contain("HTTP 500").And.NotContain(token);

            // SkyHookProxy warns about the failed request too, with its URL; only the log cleanser hides a token there
            ExceptionVerification.ExpectedWarns(2);
        }
    }
}
