using System;
using System.Linq;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Music;
using NzbDrone.Core.Music.Commands;
using NzbDrone.Core.Music.Events;

namespace NzbDrone.Core.Download.TrackedDownloads
{
    public class ImportFailedRetryService : IHandle<AlbumUpdatedEvent>, IHandle<CommandExecutedEvent>
    {
        private readonly ITrackedDownloadService _trackedDownloadService;
        private readonly IAlbumService _albumService;
        private readonly ICached<int> _changedArtists;
        private readonly Logger _logger;

        public ImportFailedRetryService(ITrackedDownloadService trackedDownloadService,
                                        IAlbumService albumService,
                                        ICacheManager cacheManager,
                                        Logger logger)
        {
            _trackedDownloadService = trackedDownloadService;
            _albumService = albumService;
            _changedArtists = cacheManager.GetCache<int>(GetType(), "changedArtists");
            _logger = logger;
        }

        // AlbumUpdatedEvent fires only when a refresh changed the album or its releases, so a failure against the old metadata may now import.
        // It fires once per changed album, so the retry waits for the refresh to finish.
        public void Handle(AlbumUpdatedEvent message)
        {
            _changedArtists.Set(message.Album.ArtistMetadataId.ToString(), message.Album.ArtistMetadataId);
        }

        public void Handle(CommandExecutedEvent message)
        {
            if (message.Command.Body is not RefreshArtistCommand and not RefreshAlbumCommand)
            {
                return;
            }

            foreach (var artistMetadataId in _changedArtists.Values.ToList())
            {
                _changedArtists.Remove(artistMetadataId.ToString());
                RetryFailedImports(artistMetadataId);
            }
        }

        private void RetryFailedImports(int artistMetadataId)
        {
            var failed = _trackedDownloadService.GetTrackedDownloads()
                .Where(t => t.State == TrackedDownloadState.ImportFailed &&
                            t.RemoteAlbum?.Artist != null &&
                            t.RemoteAlbum.Artist.ArtistMetadataId == artistMetadataId)
                .ToList();

            foreach (var trackedDownload in failed)
            {
                try
                {
                    Retry(trackedDownload);
                }
                catch (Exception e)
                {
                    _logger.Warn(e, "Couldn't retry import of '{0}'", trackedDownload.DownloadItem.Title);
                }
            }
        }

        private void Retry(TrackedDownload trackedDownload)
        {
            var albumIds = trackedDownload.RemoteAlbum.Albums?.Select(a => a.Id).ToList();

            // Nothing to import against, and develop's IsImported counts a download with no albums as imported
            if (albumIds == null || albumIds.Count == 0)
            {
                return;
            }

            // The cached albums hold the releases from before the refresh, which VerifyImport would count against.
            var albums = _albumService.GetAlbums(albumIds);

            // A manual import can finish while the albums load
            if (trackedDownload.State != TrackedDownloadState.ImportFailed)
            {
                return;
            }

            trackedDownload.RemoteAlbum.Albums = albums;

            // ImportBlocked is the failed state CompletedDownloadService.Check retries on the next monitoring pass.
            trackedDownload.State = TrackedDownloadState.ImportBlocked;

            _logger.Debug("Retrying import of '{0}' after a metadata refresh", trackedDownload.DownloadItem.Title);
        }
    }
}
