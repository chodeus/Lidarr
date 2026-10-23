using FluentAssertions;
using Lidarr.Http.Authentication;
using NUnit.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Api.Test.Authentication
{
    [TestFixture]
    public class ReturnUrlFixture : TestBase
    {
        private static bool IsLocal(string url)
        {
            return url.StartsWith("/") && !url.StartsWith("//");
        }

        [TestCase(null, "", "/")]
        [TestCase("", "/lidarr", "/lidarr/")]
        [TestCase("https://evil.example.com/", "/lidarr", "/lidarr/")]
        [TestCase("//evil.example.com/", "", "/")]
        public void should_go_home_for_a_missing_or_foreign_url(string returnUrl, string urlBase, string expected)
        {
            ReturnUrl.Resolve(urlBase, returnUrl, IsLocal).Should().Be(expected);
        }

        [TestCase("/settings", "", "/settings")]
        [TestCase("/lidarr/settings", "/lidarr", "/lidarr/settings")]
        [TestCase("/Lidarr/settings", "/lidarr", "/Lidarr/settings")]
        [TestCase("/lidarr", "/lidarr", "/lidarr")]
        public void should_keep_a_url_inside_the_url_base(string returnUrl, string urlBase, string expected)
        {
            ReturnUrl.Resolve(urlBase, returnUrl, IsLocal).Should().Be(expected);
        }

        [TestCase("/settings", "/lidarr", "/lidarr/settings")]
        [TestCase("/lidarrish/settings", "/lidarr", "/lidarr/lidarrish/settings")]
        public void should_put_a_url_outside_the_url_base_inside_it(string returnUrl, string urlBase, string expected)
        {
            ReturnUrl.Resolve(urlBase, returnUrl, IsLocal).Should().Be(expected);
        }
    }
}
