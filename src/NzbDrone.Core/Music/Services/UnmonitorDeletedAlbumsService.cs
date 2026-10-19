using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Music.Events;

namespace NzbDrone.Core.Music
{
    public class UnmonitorDeletedAlbumsService : IHandle<TrackFileDeletedEvent>,
                                                 IHandle<DiskScanCompletedEvent>,
                                                 IHandle<AlbumDeletedEvent>
    {
        private readonly IAlbumService _albumService;
        private readonly IMediaFileService _mediaFileService;
        private readonly IConfigService _configService;
        private readonly IDiskProvider _diskProvider;
        private readonly Logger _logger;

        // Albums whose files a disk scan found missing. The same scan can import them again from a new path,
        // so they are only unmonitored once it completes.
        private readonly HashSet<int> _missingFromDisk = new HashSet<int>();

        public UnmonitorDeletedAlbumsService(IAlbumService albumService,
                                             IMediaFileService mediaFileService,
                                             IConfigService configService,
                                             IDiskProvider diskProvider,
                                             Logger logger)
        {
            _albumService = albumService;
            _mediaFileService = mediaFileService;
            _configService = configService;
            _diskProvider = diskProvider;
            _logger = logger;
        }

        public void Handle(TrackFileDeletedEvent message)
        {
            if (!_configService.AutoUnmonitorPreviouslyDownloadedTracks)
            {
                return;
            }

            if (message.Reason == DeleteMediaFileReason.MissingFromDisk)
            {
                lock (_missingFromDisk)
                {
                    _missingFromDisk.Add(message.TrackFile.AlbumId);
                }
            }

            // Removing a root folder drops its files from the database but leaves them on disk.
            else if (message.Reason == DeleteMediaFileReason.Manual && !_diskProvider.FileExists(message.TrackFile.Path))
            {
                UnmonitorAlbumsWithoutFiles(new List<int> { message.TrackFile.AlbumId });
            }
        }

        public void Handle(DiskScanCompletedEvent message)
        {
            List<int> albumIds;

            lock (_missingFromDisk)
            {
                albumIds = _missingFromDisk.ToList();
                _missingFromDisk.Clear();
            }

            UnmonitorAlbumsWithoutFiles(albumIds);
        }

        public void Handle(AlbumDeletedEvent message)
        {
            lock (_missingFromDisk)
            {
                _missingFromDisk.Remove(message.Album.Id);
            }
        }

        private void UnmonitorAlbumsWithoutFiles(List<int> albumIds)
        {
            var albums = _albumService.GetAlbums(albumIds)
                .Where(a => a.Monitored && !_mediaFileService.GetFilesByAlbum(a.Id).Any())
                .ToList();

            if (!albums.Any())
            {
                return;
            }

            foreach (var album in albums)
            {
                _logger.Info("Unmonitoring album {0}, its files were deleted from disk", album);
            }

            _albumService.SetMonitored(albums.Select(a => a.Id), false);
        }
    }
}
