using System;
using System.Text;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Core.Profiles.Releases.TermMatchers;

namespace NzbDrone.Core.Profiles.Releases
{
    public interface ITermMatcherService
    {
        bool IsMatch(string term, string value);
        string MatchingTerm(string term, string value);
    }

    public class TermMatcherService : ITermMatcherService
    {
        private static readonly Logger Logger = NzbDroneLogger.GetLogger(typeof(TermMatcherService));
        private static readonly Regex WordCharacter = new Regex(@"^\w$", RegexOptions.Compiled);

        private ICached<ITermMatcher> _matcherCache;
        private ICached<ITermMatcher> _wholeWordMatcherCache;

        public TermMatcherService(ICacheManager cacheManager)
        {
            _matcherCache = cacheManager.GetCache<ITermMatcher>(GetType());
            _wholeWordMatcherCache = cacheManager.GetCache<ITermMatcher>(GetType(), "wholeWord");
        }

        public bool IsMatch(string term, string value)
        {
            return GetMatcher(term).IsMatch(value);
        }

        // A title or term can arrive composed or decomposed (Discogs and Deezer send either), so both are compared composed
        public bool IsWholeWordMatch(string term, string value)
        {
            var matcher = _wholeWordMatcherCache.Get(term, () => CreateWholeWordMatcher(term), TimeSpan.FromHours(24));

            try
            {
                return matcher.IsMatch(value?.Normalize(NormalizationForm.FormC));
            }
            catch (RegexMatchTimeoutException)
            {
                // Not a match: a term that can't decide must never take an album away
                Logger.Warn("Must Not Contain term '{0}' took too long on '{1}', keeping the album", term, value);
                return false;
            }
        }

        public string MatchingTerm(string term, string value)
        {
            return GetMatcher(term).MatchingTerm(value);
        }

        public ITermMatcher GetMatcher(string term)
        {
            return _matcherCache.Get(term, () => CreateMatcherInternal(term), TimeSpan.FromHours(24));
        }

        private ITermMatcher CreateMatcherInternal(string term)
        {
            if (PerlRegexFactory.TryCreateRegex(term, out var regex))
            {
                return new RegexTermMatcher(regex);
            }
            else
            {
                return new CaseInsensitiveTermMatcher(term);
            }
        }

        private ITermMatcher CreateWholeWordMatcher(string term)
        {
            if (PerlRegexFactory.TryCreateTermRegex(term, out var regex))
            {
                return new RegexTermMatcher(regex);
            }

            // A boundary only where the term has a word character, so "(live)" also matches right after a letter, as in "Album(Live)"
            var composed = term.Normalize(NormalizationForm.FormC);
            var start = composed.Length > 0 && WordCharacter.IsMatch(composed.AsSpan(0, 1)) ? @"(?<!\w)" : string.Empty;
            var end = composed.Length > 0 && WordCharacter.IsMatch(composed.AsSpan(composed.Length - 1)) ? @"(?!\w)" : string.Empty;

            return new RegexTermMatcher(new Regex(start + Regex.Escape(composed) + end, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled));
        }
    }
}
