using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Indexers;

namespace NzbDrone.Core.MediaFiles
{
    public static class FileExtensions
    {
        private static List<string> _archiveExtensions = new List<string>
        {
            ".7z",
            ".bz2",
            ".gz",
            ".r00",
            ".rar",
            ".tar.bz2",
            ".tar.gz",
            ".tar",
            ".tb2",
            ".tbz2",
            ".tgz",
            ".zip",
            ".zipx"
        };

        private static List<string> _executableExtensions = new List<string>
        {
            ".exe",
            ".bat",
            ".cmd",
            ".sh"
        };

        public static HashSet<string> ArchiveExtensions => new HashSet<string>(_archiveExtensions, StringComparer.OrdinalIgnoreCase);
        public static HashSet<string> ExecutableExtensions => new HashSet<string>(_executableExtensions, StringComparer.OrdinalIgnoreCase);

        public static HashSet<string> DangerousExtensions => new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".arj",
            ".lnk",
            ".lzh",
            ".ps1",
            ".scr",
            ".vbs",
            ".zipx"
        };

        public static List<string> ParseExtensions(string input)
        {
            if (input.IsNullOrWhiteSpace())
            {
                return new List<string>();
            }

            return input.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(e => e.Trim(' ', '.').Insert(0, "."))
                .ToList();
        }

        public static HashSet<string> ParseUserRejectedExtensions(string input)
        {
            return ParseExtensions(input).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        // The one place that decides which Fail Downloads type a file is: used at grab and at import
        public static Dictionary<FailDownloads, HashSet<string>> FindUnsafeExtensions(IEnumerable<string> fileNames, string userRejectedExtensions)
        {
            var groups = new Dictionary<FailDownloads, HashSet<string>>
            {
                [FailDownloads.PotentiallyDangerous] = DangerousExtensions,
                [FailDownloads.Executables] = ExecutableExtensions,
                [FailDownloads.UserDefinedExtensions] = ParseUserRejectedExtensions(userRejectedExtensions)
            };

            var found = new Dictionary<FailDownloads, HashSet<string>>();

            foreach (var extension in fileNames.Select(Path.GetExtension).Where(e => e.IsNotNullOrWhiteSpace()))
            {
                foreach (var group in groups.Where(g => g.Value.Contains(extension)))
                {
                    if (!found.TryGetValue(group.Key, out var extensions))
                    {
                        extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        found[group.Key] = extensions;
                    }

                    extensions.Add(extension);
                }
            }

            return found;
        }
    }
}
