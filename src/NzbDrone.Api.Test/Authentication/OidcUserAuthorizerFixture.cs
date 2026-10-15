using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Lidarr.Http.Authentication;
using NLog;
using NLog.Config;
using NLog.Targets;
using NUnit.Framework;

namespace NzbDrone.Api.Test.Authentication
{
    [TestFixture]
    public class OidcUserAuthorizerFixture
    {
        private static ClaimsPrincipal Principal(params (string Type, string Value)[] claims)
        {
            return new ClaimsPrincipal(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value))));
        }

        private static JsonElement UserInfo(string json)
        {
            return JsonDocument.Parse(json).RootElement;
        }

        [TestCase("subject-1")]
        [TestCase("SUBJECT-1")]
        public void should_let_in_the_configured_subject(string configuredUser)
        {
            var principal = Principal(("sub", "subject-1"));

            OidcUserAuthorizer.TryAuthorize(configuredUser, principal, null).Should().BeTrue();
            principal.FindFirst("user").Value.Should().Be(configuredUser);
        }

        [Test]
        public void should_keep_out_another_subject()
        {
            var principal = Principal(("sub", "subject-2"));

            OidcUserAuthorizer.TryAuthorize("subject-1", principal, null).Should().BeFalse();
            principal.FindFirst("user").Should().BeNull();
        }

        [Test]
        public void should_log_the_subject_not_the_email_of_a_user_it_keeps_out()
        {
            var target = new MemoryTarget("oidc-user-authorizer") { Layout = "${message}" };
            var config = LogManager.Configuration ?? new LoggingConfiguration();
            config.AddRuleForAllLevels(target, "OidcUserAuthorizer");
            LogManager.Configuration = config;

            try
            {
                var principal = Principal(("sub", "subject-2"), ("email", "user@example.com"), ("email_verified", "true"));

                OidcUserAuthorizer.TryAuthorize("subject-1", principal, null).Should().BeFalse();

                target.Logs.Should().ContainSingle().Which.Should().Contain("subject-2").And.NotContain("user@example.com");
            }
            finally
            {
                config.RemoveTarget(target.Name);
                LogManager.Configuration = config;
            }
        }

        [TestCase("true")]
        [TestCase("\"true\"")]
        public void should_let_in_a_verified_email_from_user_info(string verified)
        {
            var user = UserInfo("{\"sub\":\"subject-1\",\"email\":\"user@example.com\",\"email_verified\":" + verified + "}");

            OidcUserAuthorizer.TryAuthorize("user@example.com", Principal(("sub", "subject-1")), user).Should().BeTrue();
        }

        [TestCase("false")]
        [TestCase("\"no\"")]
        [TestCase("null")]
        public void should_keep_out_an_unverified_email_from_user_info(string verified)
        {
            var user = UserInfo("{\"sub\":\"subject-1\",\"email\":\"user@example.com\",\"email_verified\":" + verified + "}");

            OidcUserAuthorizer.TryAuthorize("user@example.com", Principal(("sub", "subject-1")), user).Should().BeFalse();
        }

        [Test]
        public void should_keep_out_an_email_user_info_leaves_unverified_even_when_the_token_says_verified()
        {
            var principal = Principal(("sub", "subject-1"), ("email", "user@example.com"), ("email_verified", "true"));
            var user = UserInfo("{\"sub\":\"subject-1\",\"email\":\"user@example.com\"}");

            OidcUserAuthorizer.TryAuthorize("user@example.com", principal, user).Should().BeFalse();
        }

        [TestCase("true", true)]
        [TestCase("false", false)]
        public void should_use_the_tokens_email_without_user_info(string verified, bool expected)
        {
            var principal = Principal(("sub", "subject-1"), ("email", "user@example.com"), ("email_verified", verified));

            OidcUserAuthorizer.TryAuthorize("user@example.com", principal, null).Should().Be(expected);
        }

        [Test]
        public void should_prefer_the_user_info_subject()
        {
            var user = UserInfo("{\"sub\":\"subject-2\"}");

            OidcUserAuthorizer.GetIdentifiers(Principal(("sub", "subject-1")), user).Should().Equal("subject-2");
        }

        [TestCase(" ")]
        [TestCase("")]
        [TestCase(null)]
        public void should_keep_everyone_out_without_a_configured_user(string configuredUser)
        {
            OidcUserAuthorizer.TryAuthorize(configuredUser, Principal(("sub", " ")), null).Should().BeFalse();
        }
    }
}
