using System;
using System.Text;
using System.Text.RegularExpressions;

namespace NzbDrone.Core.Profiles.Releases.TermMatchers
{
    // A plain term as a whole word; a title can arrive composed or decomposed (Discogs and Deezer send either), so both are compared composed
    public sealed class WholeWordTermMatcher : ITermMatcher
    {
        private static readonly Regex WordCharacter = new Regex(@"^\w$", RegexOptions.Compiled);

        private readonly Regex _regex;

        public WholeWordTermMatcher(string term)
        {
            var composed = term.Normalize(NormalizationForm.FormC);

            // A boundary only where the term has a word character, so "(live)" also matches right after a letter, as in "Album(Live)"
            var start = composed.Length > 0 && WordCharacter.IsMatch(composed.AsSpan(0, 1)) ? @"(?<!\w)" : string.Empty;
            var end = composed.Length > 0 && WordCharacter.IsMatch(composed.AsSpan(composed.Length - 1)) ? @"(?!\w)" : string.Empty;

            _regex = new Regex(start + Regex.Escape(composed) + end, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        }

        public bool IsMatch(string value)
        {
            return _regex.IsMatch(value?.Normalize(NormalizationForm.FormC));
        }

        public string MatchingTerm(string value)
        {
            return _regex.Match(value?.Normalize(NormalizationForm.FormC)).Value;
        }
    }
}
