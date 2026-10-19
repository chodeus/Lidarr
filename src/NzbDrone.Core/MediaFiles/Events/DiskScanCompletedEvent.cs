using System.Collections.Generic;
using NzbDrone.Common.Messaging;

namespace NzbDrone.Core.MediaFiles.Events
{
    public class DiskScanCompletedEvent : IEvent
    {
        // Root folders the scan cleaned and imported from in full, rather than only an artist folder within them.
        public List<string> RootFolders { get; }

        // Albums the scan matched files to, whether or not it imported them.
        public HashSet<int> MatchedAlbumIds { get; }

        public DiskScanCompletedEvent(List<string> rootFolders, HashSet<int> matchedAlbumIds)
        {
            RootFolders = rootFolders;
            MatchedAlbumIds = matchedAlbumIds;
        }
    }
}
