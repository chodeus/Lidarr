using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Music;

namespace NzbDrone.Core.Download
{
    public interface IIgnoredDownloadService
    {
        bool IgnoreDownload(TrackedDownload trackedDownload);
    }

    public class IgnoredDownloadService : IIgnoredDownloadService
    {
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;

        public IgnoredDownloadService(IEventAggregator eventAggregator,
                                      Logger logger)
        {
            _eventAggregator = eventAggregator;
            _logger = logger;
        }

        public bool IgnoreDownload(TrackedDownload trackedDownload)
        {
            // A download that never mapped to an artist, such as one with multiple matching artists, has no RemoteAlbum.
            // It is ignored by its download id alone, which is what keeps it ignored when the queue is refreshed.
            var artist = trackedDownload.RemoteAlbum?.Artist;
            var albums = trackedDownload.RemoteAlbum?.Albums ?? new List<Album>();

            if (artist == null || albums.Empty())
            {
                _logger.Debug("Ignoring download {0} that isn't matched to an artist and album", trackedDownload.DownloadItem.Title);
            }

            var downloadIgnoredEvent = new DownloadIgnoredEvent
            {
                ArtistId = artist?.Id ?? 0,
                AlbumIds = albums.Select(e => e.Id).ToList(),
                Quality = trackedDownload.RemoteAlbum?.ParsedAlbumInfo?.Quality,
                SourceTitle = trackedDownload.DownloadItem.Title,
                DownloadClientInfo = trackedDownload.DownloadItem.DownloadClientInfo,
                DownloadId = trackedDownload.DownloadItem.DownloadId,
                TrackedDownload = trackedDownload,
                Message = "Manually ignored"
            };

            _eventAggregator.PublishEvent(downloadIgnoredEvent);
            return true;
        }
    }
}
