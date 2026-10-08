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

            return input.Split(',')
                .Select(e => e.Trim().Trim('.').Trim())
                .Where(e => e.Length > 0)
                .Select(e => "." + e)
                .ToList();
        }

        public static HashSet<string> ParseUserRejectedExtensions(string input)
        {
            return ParseExtensions(input).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        public static Dictionary<FailDownloads, HashSet<string>> FindUnsafeExtensions(IEnumerable<string> fileNames, string userRejectedExtensions)
        {
            var userExtensions = ParseUserRejectedExtensions(userRejectedExtensions);
            var found = new Dictionary<FailDownloads, HashSet<string>>();

            void Add(FailDownloads type, string extension)
            {
                if (!found.TryGetValue(type, out var extensions))
                {
                    extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    found[type] = extensions;
                }

                extensions.Add(extension);
            }

            foreach (var fileName in fileNames.Where(f => f.IsNotNullOrWhiteSpace()))
            {
                var extension = Path.GetExtension(fileName);

                if (DangerousExtensions.Contains(extension))
                {
                    Add(FailDownloads.PotentiallyDangerous, extension);
                }

                if (ExecutableExtensions.Contains(extension))
                {
                    Add(FailDownloads.Executables, extension);
                }

                // Suffix match so a configured compound extension such as .foo.bar is found too
                foreach (var userExtension in userExtensions.Where(e => fileName.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
                {
                    Add(FailDownloads.UserDefinedExtensions, userExtension);
                }
            }

            return found;
        }
    }
}
