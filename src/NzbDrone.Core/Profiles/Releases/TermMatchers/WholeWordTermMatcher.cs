using System;
using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace NzbDrone.Core.Profiles.Releases.TermMatchers
{
    // A plain term as a whole word; a title can arrive composed or decomposed (Discogs and Deezer send either), so both are compared composed
    public sealed class WholeWordTermMatcher : ITermMatcher
    {
        private readonly Regex _regex;
        private readonly bool _wordStart;
        private readonly bool _wordEnd;

        public WholeWordTermMatcher(string term)
        {
            var composed = term.Normalize(NormalizationForm.FormC);

            _regex = new Regex(Regex.Escape(composed), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

            // A boundary only where the term has a word character, so "(live)" also matches right after a letter, as in "Album(Live)"
            _wordStart = WordAfter(composed, 0);
            _wordEnd = WordBefore(composed, composed.Length);
        }

        public bool IsMatch(string value)
        {
            return Find(value?.Normalize(NormalizationForm.FormC)).Success;
        }

        public string MatchingTerm(string value)
        {
            return Find(value?.Normalize(NormalizationForm.FormC)).Value;
        }

        private Match Find(string value)
        {
            // Retried one character on, as a lookaround would be, so an occurrence overlapping a rejected one is still found
            for (var match = _regex.Match(value); match.Success; match = _regex.Match(value, match.Index + 1))
            {
                if (!(_wordStart && WordBefore(value, match.Index)) && !(_wordEnd && WordAfter(value, match.Index + match.Length)))
                {
                    return match;
                }
            }

            return Match.Empty;
        }

        // Read as Unicode scalars: Regex's \w sees each half of a surrogate pair alone, so it misses letters past U+FFFF, such as U+20000
        private static bool WordBefore(string value, int index)
        {
            return Rune.DecodeLastFromUtf16(value.AsSpan(0, index), out var rune, out _) == OperationStatus.Done && IsWord(rune);
        }

        private static bool WordAfter(string value, int index)
        {
            return Rune.DecodeFromUtf16(value.AsSpan(index), out var rune, out _) == OperationStatus.Done && IsWord(rune);
        }

        // Regex's \w: letters, decimal digits, non-spacing marks and connector punctuation
        private static bool IsWord(Rune rune)
        {
            return Rune.IsLetterOrDigit(rune) || Rune.GetUnicodeCategory(rune) is UnicodeCategory.NonSpacingMark or UnicodeCategory.ConnectorPunctuation;
        }
    }
}
