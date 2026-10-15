using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.History
{
    public class ReleaseGrabHistory
    {
        public DateTime Grabbed { get; set; }
        public DateTime? Failed { get; set; }

        // Expects history as the repository returns it: newest first, with camelCase data keys
        public static ReleaseGrabHistory Find(ReleaseInfo release, List<EntityHistory> history)
        {
            var grabs = history.Where(h => h.EventType == EntityHistoryEventType.Grabbed).ToList();
            var grab = grabs.FirstOrDefault(h => SameGuid(h, release)) ?? grabs.FirstOrDefault(h => SameRelease(h, release));

            if (grab == null)
            {
                return null;
            }

            // A torrent grabbed again keeps its download ID, so an older failure belongs to an earlier grab
            var failed = grab.DownloadId.IsNullOrWhiteSpace()
                ? null
                : history.FirstOrDefault(h => h.EventType == EntityHistoryEventType.DownloadFailed && h.DownloadId == grab.DownloadId && h.Date >= grab.Date);

            return new ReleaseGrabHistory { Grabbed = grab.Date, Failed = failed?.Date };
        }

        private static bool SameGuid(EntityHistory grab, ReleaseInfo release)
        {
            return release.Guid.IsNotNullOrWhiteSpace() && grab.Data.GetValueOrDefault("guid") == release.Guid;
        }

        private static bool SameRelease(EntityHistory grab, ReleaseInfo release)
        {
            if (release is TorrentInfo torrent && torrent.InfoHash.IsNotNullOrWhiteSpace())
            {
                return torrent.InfoHash.Equals(grab.Data.GetValueOrDefault("torrentInfoHash"), StringComparison.InvariantCultureIgnoreCase);
            }

            // The same post on the same indexer; the published date is written in this format when grabbed
            return grab.SourceTitle == release.Title &&
                   release.Indexer.IsNotNullOrWhiteSpace() &&
                   release.Indexer.Equals(grab.Data.GetValueOrDefault("indexer"), StringComparison.InvariantCultureIgnoreCase) &&
                   grab.Data.GetValueOrDefault("publishedDate") == release.PublishDate.ToUniversalTime().ToString("s") + "Z";
        }
    }
}
