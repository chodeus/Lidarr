using System.Linq;
using NLog;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Music;
using NzbDrone.Core.Music.Events;

namespace NzbDrone.Core.Download.TrackedDownloads
{
    public class ImportFailedRetryService : IHandle<AlbumUpdatedEvent>
    {
        private readonly ITrackedDownloadService _trackedDownloadService;
        private readonly IAlbumService _albumService;
        private readonly Logger _logger;

        public ImportFailedRetryService(ITrackedDownloadService trackedDownloadService,
                                        IAlbumService albumService,
                                        Logger logger)
        {
            _trackedDownloadService = trackedDownloadService;
            _albumService = albumService;
            _logger = logger;
        }

        // AlbumUpdatedEvent fires only when a refresh changed the album or its releases, so a failure against the old metadata may now import.
        public void Handle(AlbumUpdatedEvent message)
        {
            var failed = _trackedDownloadService.GetTrackedDownloads()
                .Where(t => t.State == TrackedDownloadState.ImportFailed &&
                            t.RemoteAlbum?.Artist != null &&
                            t.RemoteAlbum.Artist.ArtistMetadataId == message.Album.ArtistMetadataId)
                .ToList();

            foreach (var trackedDownload in failed)
            {
                // The cached albums hold the releases from before the refresh, which VerifyImport would count against.
                var albumIds = trackedDownload.RemoteAlbum.Albums?.Select(a => a.Id).ToList();

                if (albumIds?.Any() == true)
                {
                    trackedDownload.RemoteAlbum.Albums = _albumService.GetAlbums(albumIds);
                }

                // ImportBlocked is the failed state CompletedDownloadService.Check retries on the next monitoring pass.
                trackedDownload.State = TrackedDownloadState.ImportBlocked;

                _logger.Debug("Retrying import of '{0}' after a metadata refresh of {1}", trackedDownload.DownloadItem.Title, message.Album);
            }
        }
    }
}
