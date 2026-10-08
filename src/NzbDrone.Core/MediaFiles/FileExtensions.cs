using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;

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
    }
}
