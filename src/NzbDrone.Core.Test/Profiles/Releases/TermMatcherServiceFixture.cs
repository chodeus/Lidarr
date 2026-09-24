using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Profiles.Releases;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Profiles.Releases
{
    [TestFixture]
    public class TermMatcherServiceFixture : CoreTest<TermMatcherService>
    {
        [TestCase("live", "Live at Wembley")]
        [TestCase("live", "Album (Live)")]
        [TestCase("LIVE", "live")]
        [TestCase("greatest hits", "The Greatest Hits")]
        [TestCase("(live)", "Album (Live)")]
        [TestCase("Live // Unplugged", "MTV Live // Unplugged")]
        public void should_match_term_as_whole_word(string term, string title)
        {
            Subject.IsWholeWordMatch(term, title).Should().BeTrue();
        }

        [TestCase("Live // Unplugged", "Greatest Hits")]
        [TestCase("Live 8/15/95", "Track 15")]
        [TestCase("Rock/Pop/mix", "Pop Hits")]
        public void should_treat_term_with_slashes_as_plain_text(string term, string title)
        {
            Subject.IsWholeWordMatch(term, title).Should().BeFalse();
        }

        [Test]
        [SetCulture("tr-TR")]
        public void should_ignore_case_regardless_of_culture()
        {
            Subject.IsWholeWordMatch("live forever", "LIVE FOREVER").Should().BeTrue();
        }

        [TestCase("live", "Alive")]
        [TestCase("live", "Oliver")]
        [TestCase("live", "Deliverance")]
        [TestCase("christmas", "Christmastime")]
        public void should_not_match_term_inside_a_word(string term, string title)
        {
            Subject.IsWholeWordMatch(term, title).Should().BeFalse();
        }

        [TestCase("/christmas/i", "Christmastime")]
        [TestCase("/^live/i", "Live at Wembley")]
        public void should_match_regex_term_as_written(string term, string title)
        {
            Subject.IsWholeWordMatch(term, title).Should().BeTrue();
        }
    }
}
