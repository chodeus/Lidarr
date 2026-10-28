using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NzbDrone.Common;
using NzbDrone.Core.MediaFiles;

namespace NzbDrone.Core.Extras.Files
{
    public static class ExtraFileNames
    {
        // The longest name wins, so "Mr. Blue Sky.lrc" goes to "Mr. Blue Sky.flac", not "Mr.flac"; a tie ("Song.flac" and
        // "Song.wav" for "Song.lrc") can't say which, so it has none
        public static TrackFile FindTrackFile(string extraPath, IEnumerable<TrackFile> trackFiles)
        {
            var matches = trackFiles.Where(f => IsNamedAfter(extraPath, f.Path))
                                    .GroupBy(f => Path.GetFileNameWithoutExtension(f.Path).Length)
                                    .OrderByDescending(g => g.Key)
                                    .FirstOrDefault()?
                                    .ToList();

            return matches?.Count == 1 ? matches[0] : null;
        }

        // ExtraFileManager names an extra after its track file: beside it, the track file's name, a dot, then the rest
        private static bool IsNamedAfter(string extraPath, string trackFilePath)
        {
            return PathEqualityComparer.Instance.Equals(Path.GetDirectoryName(extraPath), Path.GetDirectoryName(trackFilePath)) &&
                   Path.GetFileName(extraPath).StartsWith(Path.GetFileNameWithoutExtension(trackFilePath) + ".", StringComparison.InvariantCultureIgnoreCase);
        }
    }
}
