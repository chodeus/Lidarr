using System;
using System.Text.RegularExpressions;
using NzbDrone.Common.Cache;
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

        public bool IsWholeWordMatch(string term, string value)
        {
            return _wholeWordMatcherCache.Get(term, () => CreateWholeWordMatcher(term), TimeSpan.FromHours(24)).IsMatch(value);
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

            // Not \b, which fails when the term starts or ends with punctuation, such as "(live)"
            return new RegexTermMatcher(new Regex(@"(?<!\w)" + Regex.Escape(term) + @"(?!\w)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled));
        }
    }
}
