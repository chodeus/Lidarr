using System;
using System.IO;
using NzbDrone.Common;

namespace NzbDrone.Core.Extras.Files
{
    public static class ExtraFileNames
    {
        // ExtraFileManager names an extra after its track file: beside it, the track file's name, a dot, then the rest
        public static bool IsNamedAfter(string extraPath, string trackFilePath)
        {
            return PathEqualityComparer.Instance.Equals(Path.GetDirectoryName(extraPath), Path.GetDirectoryName(trackFilePath)) &&
                   Path.GetFileName(extraPath).StartsWith(Path.GetFileNameWithoutExtension(trackFilePath) + ".", StringComparison.InvariantCultureIgnoreCase);
        }
    }
}
