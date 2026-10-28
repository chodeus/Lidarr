using System;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Profiles.Releases;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Profiles.Releases
{
    [TestFixture]
    public class WholeWordTermMatcherFixture : CoreTest<TermMatcherService>
    {
        [Test]
        public void a_regex_term_that_backtracks_badly_times_out_as_no_match()
        {
            var title = new string('a', 30) + "!";
            var task = Task.Run(() => Subject.IsWholeWordMatch("/(a+)+$/", title));

            task.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue("a refresh must not hang on one title");
            task.Result.Should().BeFalse();
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void a_term_starting_with_punctuation_matches_after_a_letter()
        {
            Subject.IsWholeWordMatch("(live)", "Album(Live)").Should().BeTrue();
        }

        [Test]
        public void a_composed_term_matches_a_decomposed_title()
        {
            Subject.IsWholeWordMatch("Café", "Café Sessions").Should().BeTrue();
        }

        [TestCase("/^....$/", false)]
        [TestCase("/Cafe\u0301/", true)]
        public void a_regex_term_sees_a_decomposed_title_as_sent(string term, bool matches)
        {
            Subject.IsWholeWordMatch(term, "Cafe\u0301").Should().Be(matches);
        }

        [TestCase("/live/", false)]
        [TestCase("/live/i", true)]
        public void a_regex_term_is_matched_as_written(string term, bool matches)
        {
            Subject.IsWholeWordMatch(term, "Live at the Venue").Should().Be(matches);
        }

        [TestCase("live", "Album (Live)", true)]
        [TestCase("live", "Alive", false)]
        [TestCase("live", "Livestream", false)]
        public void a_plain_term_still_needs_word_boundaries(string term, string title, bool matches)
        {
            Subject.IsWholeWordMatch(term, title).Should().Be(matches);
        }
    }
}
