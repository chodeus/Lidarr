using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Common.TPL;
using NzbDrone.Core.MetadataSource.Sources;
using NzbDrone.Core.MetadataSource.Sources.Discogs;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource.Sources.Discogs
{
    [TestFixture]
    public class DiscogsApiFixture : CoreTest<DiscogsApi>
    {
        private const string Token = "placeholder-token";

        private readonly List<HttpRequest> _requests = new();

        [SetUp]
        public void Setup()
        {
            _requests.Clear();
        }

        private void GivenResponse<T>(HttpStatusCode status, Func<HttpRequest, string> json)
            where T : new()
        {
            GivenResponses<T>((status, new HttpHeader(), json));
        }

        private void GivenResponses<T>(params (HttpStatusCode Status, HttpHeader Headers, Func<HttpRequest, string> Json)[] responses)
            where T : new()
        {
            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Get<T>(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(r => _requests.Add(r))
                .Returns<HttpRequest>(r =>
                {
                    var (status, headers, json) = responses[Math.Min(_requests.Count, responses.Length) - 1];
                    return new HttpResponse<T>(new HttpResponse(r, headers, json(r), status));
                });
        }

        private static HttpHeader Header(string name, string value)
        {
            var headers = new HttpHeader();
            headers.Add(name, value);
            return headers;
        }

        private void VerifyPause(TimeSpan? pause)
        {
            Mocker.GetMock<IRateLimitService>().Verify(r => r.WaitAndPulse("api.discogs.com", It.IsAny<TimeSpan>()), pause.HasValue ? Times.Once() : Times.Never());

            if (pause.HasValue)
            {
                Mocker.GetMock<IRateLimitService>().Verify(r => r.WaitAndPulse("api.discogs.com", pause.Value), Times.Once());
            }
        }

        [Test]
        public void a_discogs_not_found_is_null()
        {
            GivenResponse<DiscogsReleaseResource>(HttpStatusCode.NotFound, r => "{\"message\":\"Release not found.\"}");

            Subject.GetRelease(1, Token).Should().BeNull();
        }

        [TestCase(HttpStatusCode.Unauthorized)]
        [TestCase(HttpStatusCode.InternalServerError)]
        public void other_http_errors_mean_discogs_is_unavailable(HttpStatusCode status)
        {
            GivenResponse<DiscogsReleaseResource>(status, r => "{\"message\":\"placeholder\"}");

            Assert.Throws<MetadataSourceUnavailableException>(() => Subject.GetRelease(1, Token));
        }

        [Test]
        public void a_failed_request_means_discogs_is_unavailable()
        {
            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Get<DiscogsReleaseResource>(It.IsAny<HttpRequest>()))
                .Throws(new WebException("Connection refused"));

            Assert.Throws<MetadataSourceUnavailableException>(() => Subject.GetRelease(1, Token));
        }

        [Test]
        public void the_token_goes_in_a_header_and_sets_the_faster_rate_limit()
        {
            GivenResponse<DiscogsReleaseResource>(HttpStatusCode.OK, r => "{\"id\":1}");

            Subject.GetRelease(1, Token);
            Subject.GetRelease(1, null);

            _requests[0].Headers.GetSingleValue("Authorization").Should().Be("Discogs token=" + Token);
            _requests[0].Url.FullUri.Should().NotContain(Token);
            _requests[1].Headers.GetSingleValue("Authorization").Should().BeNull();
            _requests[0].RateLimit.Should().BeLessThan(_requests[1].RateLimit);
            _requests[0].RateLimit.TotalSeconds.Should().BeGreaterThanOrEqualTo(1);
        }

        [TestCase("0", 60)]
        [TestCase("5", null)]
        public void a_spent_rate_limit_pauses_until_the_window_resets(string remaining, int? pauseSeconds)
        {
            GivenResponses<DiscogsReleaseResource>((HttpStatusCode.OK, Header("X-Discogs-Ratelimit-Remaining", remaining), r => "{\"id\":1}"));

            Subject.GetRelease(1, Token).Id.Should().Be(1);

            VerifyPause(pauseSeconds.HasValue ? TimeSpan.FromSeconds(pauseSeconds.Value) : null);
        }

        [TestCase(null, 60)]
        [TestCase("7", 7)]
        public void too_many_requests_waits_and_retries_once(string retryAfter, int pauseSeconds)
        {
            var tooMany = retryAfter == null ? new HttpHeader() : Header("Retry-After", retryAfter);
            GivenResponses<DiscogsReleaseResource>(
                (HttpStatusCode.TooManyRequests, tooMany, r => "{\"message\":\"placeholder\"}"),
                (HttpStatusCode.OK, new HttpHeader(), r => "{\"id\":1}"));

            Subject.GetRelease(1, Token).Id.Should().Be(1);

            _requests.Should().HaveCount(2);
            VerifyPause(TimeSpan.FromSeconds(pauseSeconds));
        }

        [Test]
        public void too_many_requests_twice_means_discogs_is_unavailable()
        {
            GivenResponses<DiscogsReleaseResource>((HttpStatusCode.TooManyRequests, Header("X-Discogs-Ratelimit-Remaining", "0"), r => "{\"message\":\"placeholder\"}"));

            Assert.Throws<MetadataSourceUnavailableException>(() => Subject.GetRelease(1, Token));

            _requests.Should().HaveCount(2);
            VerifyPause(TimeSpan.FromSeconds(60));
        }

        [Test]
        public void artist_releases_are_read_page_by_page()
        {
            GivenResponse<DiscogsArtistReleasesPageResource>(HttpStatusCode.OK, r => r.Url.Query.Contains("page=1&")
                ? "{\"pagination\":{\"page\":1,\"pages\":2},\"releases\":[{\"id\":1}]}"
                : "{\"pagination\":{\"page\":2,\"pages\":2},\"releases\":[{\"id\":2}]}");

            Subject.GetArtistReleases(3, Token).Select(r => r.Id).Should().Equal(1, 2);

            _requests.Should().HaveCount(2);
        }
    }
}
