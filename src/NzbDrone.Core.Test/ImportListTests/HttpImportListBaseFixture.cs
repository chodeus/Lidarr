using System.Net;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.HeadphonesImport;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.ImportListTests
{
    [TestFixture]
    public class HttpImportListBaseFixture : CoreTest<HeadphonesImport>
    {
        [SetUp]
        public void Setup()
        {
            Subject.Definition = new ImportListDefinition
            {
                Id = 1,
                Name = "Headphones",
                Settings = new HeadphonesImportSettings { BaseUrl = "http://headphones.test", ApiKey = "key" }
            };
        }

        [Test]
        public void should_not_report_failure_when_list_fetches()
        {
            Mocker.GetMock<IHttpClient>()
                .Setup(v => v.Execute(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest r) => new HttpResponse(r, new HttpHeader(), "[{\"ArtistName\": \"Artist\", \"ArtistId\": \"artist-mbid\"}]"));

            var result = Subject.Fetch();

            result.AnyFailure.Should().BeFalse();
            result.Items.Should().ContainSingle(i => i.ArtistMusicBrainzId == "artist-mbid");
        }

        [Test]
        public void should_report_failure_when_list_errors()
        {
            Mocker.GetMock<IHttpClient>()
                .Setup(v => v.Execute(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest r) => throw new HttpException(new HttpResponse(r, new HttpHeader(), string.Empty, HttpStatusCode.InternalServerError)));

            var result = Subject.Fetch();

            result.AnyFailure.Should().BeTrue();
            result.Items.Should().BeEmpty();

            ExceptionVerification.IgnoreWarns();
        }
    }
}
