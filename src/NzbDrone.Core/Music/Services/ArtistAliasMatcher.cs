using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Parser;

namespace NzbDrone.Core.Music
{
    internal static class ArtistAliasMatcher
    {
        // GetAllArtists hands out one list until its cache is cleared or expires, so each list's aliases are cleaned once
        private static readonly ConditionalWeakTable<List<Artist>, ILookup<string, Artist>> ArtistsByCleanAlias = new ConditionalWeakTable<List<Artist>, ILookup<string, Artist>>();

        public static Artist FindByNameOrAlias(List<Artist> artists, string title)
        {
            var cleanTitle = title.CleanArtistName();

            // An artist's own name, with or without a leading "The", wins over another artist's alias; fuzzy matching can miss it for a short name
            var ownNames = new List<string> { cleanTitle, "the" + cleanTitle };

            if (title.StartsWith("The ", StringComparison.CurrentCultureIgnoreCase))
            {
                ownNames.Add(title.Substring(4).CleanArtistName());
            }

            if (cleanTitle.IsNullOrWhiteSpace())
            {
                return null;
            }

            var named = artists.Where(a => ownNames.Contains(a.CleanName)).ToList();

            if (named.Any())
            {
                return named.Count == 1 ? named[0] : null;
            }

            var matches = ArtistsByCleanAlias.GetValue(artists, CleanAliases)[cleanTitle].Distinct().ToList();

            return matches.Count == 1 ? matches[0] : null;
        }

        private static ILookup<string, Artist> CleanAliases(List<Artist> artists)
        {
            return artists.SelectMany(a => a.Metadata.Value.Aliases.Select(alias => (Alias: alias.CleanArtistName(), Artist: a)))
                          .ToLookup(p => p.Alias, p => p.Artist);
        }
    }
}
