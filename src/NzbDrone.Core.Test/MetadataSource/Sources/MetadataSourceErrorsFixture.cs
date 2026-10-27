using System;
using System.Net;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.MetadataSource.SkyHook;
using NzbDrone.Core.MetadataSource.Sources;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource.Sources
{
    [TestFixture]
    public class MetadataSourceErrorsFixture : CoreTest
    {
        private const string Token = "s3cr3ttoken";
        private const string Url = "https://metadata.example.invalid/" + Token + "/api/v0.4/artist/1";

        [Test]
        public void should_describe_a_web_page_answer_by_its_status_and_content_not_its_url()
        {
            var request = new HttpRequest(Url);
            var headers = new HttpHeader { ContentType = "text/html" };
            var html = new UnexpectedHtmlContentException(new HttpResponse(request, headers, "<html></html>", HttpStatusCode.OK));

            MetadataSourceErrors.Describe(new SkyHookException("Search failed. {0}", html, html.Message))
                .Should().Be("HTTP 200, browser content instead of API data");
        }

        [Test]
        public void should_describe_an_unknown_failure_by_its_type_not_its_message()
        {
            var malformed = new ArgumentException("Uri didn't match expected pattern: " + Url);

            MetadataSourceErrors.Describe(new SkyHookException("Search failed. Invalid response received from LidarrAPI. {0}", malformed, malformed.Message))
                .Should().Be(nameof(ArgumentException));
        }

        [Test]
        public void should_describe_an_error_that_wraps_nothing_by_its_type_not_its_message()
        {
            MetadataSourceErrors.Describe(new SkyHookException("Search failed at " + Url))
                .Should().Be(nameof(SkyHookException));
        }

        [Test]
        public void should_describe_an_http_error_without_a_response_by_its_type()
        {
            var http = new HttpException(new HttpRequest(Url), null, "HTTP request failed at " + Url);

            MetadataSourceErrors.Describe(new SkyHookException("Search failed. {0}", http, http.Message))
                .Should().Be(nameof(HttpException));
        }
    }
}
