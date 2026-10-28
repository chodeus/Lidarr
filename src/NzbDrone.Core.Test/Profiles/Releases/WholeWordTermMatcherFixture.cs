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

        [TestCase("\U00020000", "x \U00020000 y", true)]
        [TestCase("\U00020000", "x\U00020000 y", false)]
        [TestCase("\U00020000", "x \U00020000y", false)]
        [TestCase("live", "\U0001D400live", false)]
        [TestCase("live", "live\U0001D400", false)]
        [TestCase("live", "\U0001F3B5live\U0001F3B5", true)]
        public void a_letter_past_u_ffff_is_a_word_character_but_an_emoji_is_not(string term, string title, bool matches)
        {
            Subject.IsWholeWordMatch(term, title).Should().Be(matches);
        }

        [TestCase("live", "live2", false)]
        [TestCase("live", "live̲", false)]
        [TestCase("live", "live_", false)]
        [TestCase("live", "live-", true)]
        public void a_word_character_is_a_letter_digit_mark_or_connector(string term, string title, bool matches)
        {
            Subject.IsWholeWordMatch(term, title).Should().Be(matches);
        }

        [Test]
        public void an_occurrence_overlapping_a_rejected_one_still_matches()
        {
            Subject.IsWholeWordMatch("a-a", "xa-a-a").Should().BeTrue();
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
