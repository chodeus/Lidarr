using System;

namespace NzbDrone.Core.MetadataSource.Sources
{
    public static class MusicBrainzIds
    {
        public static bool IsMusicBrainzId(string foreignId)
        {
            return Guid.TryParseExact(foreignId, "D", out _);
        }
    }
}
