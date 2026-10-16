using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.History;

namespace NzbDrone.Core.Download.TrackedDownloads
{
    public interface ITrackedDownloadAlreadyImported
    {
        bool IsImported(TrackedDownload trackedDownload, List<EntityHistory> historyItems);
    }

    public class TrackedDownloadAlreadyImported : ITrackedDownloadAlreadyImported
    {
        private readonly Logger _logger;

        public TrackedDownloadAlreadyImported(Logger logger)
        {
            _logger = logger;
        }

        public bool IsImported(TrackedDownload trackedDownload, List<EntityHistory> historyItems)
        {
            _logger.Trace("Checking if all items for '{0}' have been imported", trackedDownload.DownloadItem.Title);

            if (historyItems.Empty())
            {
                _logger.Trace("No history for {0}", trackedDownload.DownloadItem.Title);
                return false;
            }

            // All() below is true for an empty list, which would count an unmapped download as imported
            if (trackedDownload.RemoteAlbum?.Albums == null || trackedDownload.RemoteAlbum.Albums.Empty())
            {
                _logger.Trace("No albums mapped for {0}", trackedDownload.DownloadItem.Title);
                return false;
            }

            var allAlbumsImportedInHistory = trackedDownload.RemoteAlbum.Albums.All(album =>
            {
                var lastHistoryItem = historyItems.FirstOrDefault(h => h.AlbumId == album.Id);

                if (lastHistoryItem == null)
                {
                    _logger.Trace($"No history for album: {album}");
                    return false;
                }

                _logger.Trace($"Last event for album: {album} is: {lastHistoryItem.EventType}");

                return new[] { EntityHistoryEventType.DownloadImported, EntityHistoryEventType.TrackFileImported }.Contains(lastHistoryItem.EventType);
            });

            _logger.Trace("All albums for '{0}' have been imported: {1}", trackedDownload.DownloadItem.Title, allAlbumsImportedInHistory);

            return allAlbumsImportedInHistory;
        }
    }
}
