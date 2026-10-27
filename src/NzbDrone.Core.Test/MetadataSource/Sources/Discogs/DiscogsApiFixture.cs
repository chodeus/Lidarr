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
        private const string CreditedRelease = "{\"id\":1,\"artists\":[{\"id\":3,\"name\":\"Placeholder Artist\"}]}";

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

        [TestCase(HttpStatusCode.Forbidden)]
        [TestCase(HttpStatusCode.InternalServerError)]
        public void other_http_errors_mean_discogs_is_unavailable(HttpStatusCode status)
        {
            GivenResponse<DiscogsReleaseResource>(status, r => "{\"message\":\"placeholder\"}");

            Assert.Throws<MetadataSourceUnavailableException>(() => Subject.GetRelease(1, Token));
        }

        [Test]
        public void a_rejected_token_is_its_own_failure()
        {
            GivenResponse<DiscogsReleaseResource>(HttpStatusCode.Unauthorized, r => "{\"message\":\"placeholder\"}");

            Assert.Throws<DiscogsTokenRejectedException>(() => Subject.GetRelease(1, Token));
        }

        [Test]
        public void a_master_or_release_without_an_artist_is_unreadable()
        {
            GivenResponse<DiscogsReleaseResource>(HttpStatusCode.OK, r => "{\"id\":1,\"artists\":[]}");
            GivenResponse<DiscogsMasterResource>(HttpStatusCode.OK, r => "{\"id\":2}");

            Assert.Throws<MetadataSourceUnavailableException>(() => Subject.GetRelease(1, Token));
            Assert.Throws<MetadataSourceUnavailableException>(() => Subject.GetMaster(2, Token));
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
            GivenResponse<DiscogsReleaseResource>(HttpStatusCode.OK, r => CreditedRelease);

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
            GivenResponses<DiscogsReleaseResource>((HttpStatusCode.OK, Header("X-Discogs-Ratelimit-Remaining", remaining), r => CreditedRelease));

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
                (HttpStatusCode.OK, new HttpHeader(), r => CreditedRelease));

            Subject.GetRelease(1, Token).Id.Should().Be(1);

            _requests.Should().HaveCount(2);
            VerifyPause(TimeSpan.FromSeconds(pauseSeconds));
        }

        [Test]
        public void a_repeated_rate_header_uses_its_first_value()
        {
            var tooMany = Header("Retry-After", "7");
            tooMany.Add("Retry-After", "7");
            var spent = Header("X-Discogs-Ratelimit-Remaining", "0");
            spent.Add("X-Discogs-Ratelimit-Remaining", "0");
            GivenResponses<DiscogsReleaseResource>(
                (HttpStatusCode.TooManyRequests, tooMany, r => "{\"message\":\"placeholder\"}"),
                (HttpStatusCode.OK, spent, r => CreditedRelease));

            Subject.GetRelease(1, Token).Id.Should().Be(1);

            Mocker.GetMock<IRateLimitService>().Verify(r => r.WaitAndPulse("api.discogs.com", TimeSpan.FromSeconds(7)), Times.Once());
            Mocker.GetMock<IRateLimitService>().Verify(r => r.WaitAndPulse("api.discogs.com", TimeSpan.FromSeconds(60)), Times.Once());
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
                ? Releases(2, "Main:1")
                : Releases(2, "Main:2"));

            Subject.GetArtistOwnReleases(3, Token).Select(r => r.Id).Should().Equal(1, 2);

            _requests.Should().HaveCount(2);
        }

        [Test]
        public void own_releases_stop_at_the_first_other_credit()
        {
            GivenResponse<DiscogsArtistReleasesPageResource>(HttpStatusCode.OK, r => r.Url.Query.Contains("page=1&")
                ? Releases(3, "Main:1")
                : Releases(3, "Main:2", "Appearance:3"));

            Subject.GetArtistOwnReleases(3, Token).Select(r => r.Id).Should().Equal(1, 2);

            _requests.Should().HaveCount(2);
        }

        [Test]
        public void a_list_with_own_releases_after_other_credits_is_read_in_full()
        {
            GivenResponse<DiscogsArtistReleasesPageResource>(HttpStatusCode.OK, r => Releases(1, "Main:1", "Appearance:2", "Main:3"));

            Subject.GetArtistOwnReleases(3, Token).Select(r => r.Id).Should().Equal(1, 3);
        }

        [Test]
        public void a_list_starting_with_other_credits_is_read_in_full()
        {
            GivenResponse<DiscogsArtistReleasesPageResource>(HttpStatusCode.OK, r => r.Url.Query.Contains("page=1&")
                ? Releases(2, "Appearance:1")
                : Releases(2, "Main:2"));

            Subject.GetArtistOwnReleases(3, Token).Select(r => r.Id).Should().Equal(2);
        }

        [Test]
        public void the_full_read_after_a_broken_own_release_order_asks_for_each_page_once()
        {
            GivenResponse<DiscogsArtistReleasesPageResource>(HttpStatusCode.OK, r => r.Url.Query.Contains("page=1&")
                ? Releases(2, "Main:1")
                : Releases(2, "Main:2", "Appearance:3", "Main:4"));

            Subject.GetArtistOwnReleases(3, Token).Select(r => r.Id).Should().Equal(1, 2, 4);

            _requests.Select(r => r.Url.Query).Should().Equal("page=1&per_page=100", "page=2&per_page=100");
        }

        [Test]
        public void a_long_list_whose_own_releases_end_early_is_read_up_to_them()
        {
            GivenResponse<DiscogsArtistReleasesPageResource>(HttpStatusCode.OK, r => r.Url.Query.Contains("page=1&")
                ? Releases(150, "Main:1")
                : r.Url.Query.Contains("page=101&") ? Releases(150, "Appearance:101") : Releases(150, "Main:2", "Appearance:3"));

            Subject.GetArtistOwnReleases(3, Token).Select(r => r.Id).Should().Equal(1, 2);

            _requests.Should().HaveCount(3);
        }

        [Test]
        public void own_releases_past_the_page_limit_fail_after_two_requests()
        {
            GivenResponse<DiscogsArtistReleasesPageResource>(HttpStatusCode.OK, r => Releases(150, "Main:1"));

            Assert.Throws<MetadataSourceUnavailableException>(() => Subject.GetArtistOwnReleases(3, Token));

            _requests.Should().HaveCount(2);
        }

        [Test]
        public void bootlegs_are_read_from_the_last_page_back_to_the_first_other_credit()
        {
            GivenResponse<DiscogsArtistReleasesPageResource>(HttpStatusCode.OK, r => r.Url.Query.Contains("page=1&") ? Releases(4, "Main:1")
                : r.Url.Query.Contains("page=2&") ? Releases(4, "Appearance:2")
                : r.Url.Query.Contains("page=3&") ? Releases(4, "Appearance:3", "UnofficialRelease:4")
                : Releases(4, "UnofficialRelease:5"));

            Subject.GetArtistBootlegs(3, Token).Select(r => r.Id).Should().Equal(4, 5);

            _requests.Select(r => r.Url.Query).Should().Equal("page=1&per_page=100", "page=4&per_page=100", "page=3&per_page=100");
        }

        [Test]
        public void a_one_page_list_is_read_once_for_bootlegs()
        {
            GivenResponse<DiscogsArtistReleasesPageResource>(HttpStatusCode.OK, r => Releases(1, "Main:1", "UnofficialRelease:2"));

            Subject.GetArtistBootlegs(3, Token).Select(r => r.Id).Should().Equal(2);

            _requests.Should().HaveCount(1);
        }

        [Test]
        public void a_list_with_bootlegs_before_other_credits_is_read_in_full_for_bootlegs()
        {
            GivenResponse<DiscogsArtistReleasesPageResource>(HttpStatusCode.OK, r => Releases(1, "UnofficialRelease:1", "Main:2", "UnofficialRelease:3"));

            Subject.GetArtistBootlegs(3, Token).Select(r => r.Id).Should().Equal(1, 3);
        }

        [Test]
        public void the_full_read_after_a_broken_bootleg_order_asks_for_each_page_once()
        {
            GivenResponse<DiscogsArtistReleasesPageResource>(HttpStatusCode.OK, r => r.Url.Query.Contains("page=1&") ? Releases(3, "Main:1", "UnofficialRelease:2")
                : r.Url.Query.Contains("page=2&") ? Releases(3, "Appearance:3")
                : Releases(3, "UnofficialRelease:4"));

            Subject.GetArtistBootlegs(3, Token).Select(r => r.Id).Should().Equal(2, 4);

            _requests.Select(r => r.Url.Query).Should().Equal("page=1&per_page=100", "page=3&per_page=100", "page=2&per_page=100");
        }

        [Test]
        public void bootlegs_past_the_page_limit_fail()
        {
            GivenResponse<DiscogsArtistReleasesPageResource>(HttpStatusCode.OK, r => Releases(150, "UnofficialRelease:1"));

            Assert.Throws<MetadataSourceUnavailableException>(() => Subject.GetArtistBootlegs(3, Token));

            _requests.Should().HaveCount(101);
        }

        private static string Releases(int pages, params string[] entries)
        {
            var releases = entries.Select(e => e.Split(':')).Select(e => $"{{\"id\":{e[1]},\"role\":\"{e[0]}\"}}");

            return $"{{\"pagination\":{{\"pages\":{pages}}},\"releases\":[{string.Join(",", releases)}]}}";
        }

        [Test]
        public void a_missing_first_page_means_discogs_is_unavailable()
        {
            GivenResponse<DiscogsVersionsPageResource>(HttpStatusCode.NotFound, r => "{\"message\":\"Master not found.\"}");

            Assert.Throws<MetadataSourceUnavailableException>(() => Subject.GetMasterVersions(3, Token));
        }

        [Test]
        public void a_missing_later_page_means_discogs_is_unavailable()
        {
            GivenResponses<DiscogsArtistReleasesPageResource>(
                (HttpStatusCode.OK, new HttpHeader(), r => Releases(2, "Main:1")),
                (HttpStatusCode.NotFound, new HttpHeader(), r => "{\"message\":\"Artist not found.\"}"));

            Assert.Throws<MetadataSourceUnavailableException>(() => Subject.GetArtistOwnReleases(3, Token));
        }

        [Test]
        public void a_list_past_the_page_limit_fails_after_its_first_page()
        {
            GivenResponse<DiscogsVersionsPageResource>(HttpStatusCode.OK, r => "{\"pagination\":{\"page\":1,\"pages\":101},\"versions\":[{\"id\":1}]}");

            Assert.Throws<MetadataSourceUnavailableException>(() => Subject.GetMasterVersions(3, Token));

            _requests.Should().HaveCount(1);
        }
    }
}
