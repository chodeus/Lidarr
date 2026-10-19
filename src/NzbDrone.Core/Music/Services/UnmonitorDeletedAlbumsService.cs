using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.Music
{
    public interface IUnmonitorDeletedAlbumsService
    {
        // Whether a disk scan found the album's files missing, and has yet to decide whether to unmonitor it.
        bool IsMissingFromDisk(int albumId);
    }

    public class UnmonitorDeletedAlbumsService : IUnmonitorDeletedAlbumsService,
                                                 IHandle<TrackFileDeletedEvent>,
                                                 IHandle<DiskScanCompletedEvent>,
                                                 IHandle<ModelEvent<RootFolder>>
    {
        private readonly IAlbumService _albumService;
        private readonly IMediaFileService _mediaFileService;
        private readonly IConfigService _configService;
        private readonly IDiskProvider _diskProvider;
        private readonly Logger _logger;

        // Folders that held the files disk scans found missing, by album. A scan can import the files again from
        // anywhere in their root folder, so an album is only unmonitored once a scan of that whole root folder completes.
        private readonly Dictionary<int, HashSet<string>> _missingFromDisk = new Dictionary<int, HashSet<string>>();

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

        public bool IsMissingFromDisk(int albumId)
        {
            lock (_missingFromDisk)
            {
                return _missingFromDisk.ContainsKey(albumId);
            }
        }

        // After the handlers that broadcast the deleted file's album, which was loaded while it was still monitored.
        [EventHandleOrder(EventHandleOrder.Last)]
        public void Handle(TrackFileDeletedEvent message)
        {
            if (!_configService.AutoUnmonitorPreviouslyDownloadedTracks)
            {
                return;
            }

            var trackFile = message.TrackFile;

            if (message.Reason == DeleteMediaFileReason.MissingFromDisk)
            {
                lock (_missingFromDisk)
                {
                    if (!_missingFromDisk.TryGetValue(trackFile.AlbumId, out var folders))
                    {
                        folders = new HashSet<string>(PathEqualityComparer.Instance);
                        _missingFromDisk.Add(trackFile.AlbumId, folders);
                    }

                    folders.Add(trackFile.Path.GetParentPath());
                }
            }

            // Lidarr deletes a file from disk before it drops it from the database, so one still on disk was not deleted.
            else if (message.Reason == DeleteMediaFileReason.Manual && !_diskProvider.FileExists(trackFile.Path))
            {
                UnmonitorAlbumWithoutFiles(trackFile.AlbumId);
            }
        }

        public void Handle(DiskScanCompletedEvent message)
        {
            if (!_configService.AutoUnmonitorPreviouslyDownloadedTracks)
            {
                lock (_missingFromDisk)
                {
                    _missingFromDisk.Clear();
                }

                return;
            }

            // Files the scan matched to an album but did not import, such as files moved to where they no longer match
            // well enough, may still be that album's. Later scans skip them while they are unchanged, so this one decides.
            List<int> albumIds;

            lock (_missingFromDisk)
            {
                albumIds = _missingFromDisk
                    .Where(x => message.MatchedAlbumIds.Contains(x.Key) ||
                                x.Value.All(folder => message.RootFolders.Any(root => root.PathEquals(folder) || root.IsParentPath(folder))))
                    .Select(x => x.Key)
                    .ToList();
            }

            foreach (var albumId in albumIds)
            {
                if (message.MatchedAlbumIds.Contains(albumId))
                {
                    _logger.Debug("Not unmonitoring album {0}, the scan matched files to it that it did not import", albumId);
                }
                else
                {
                    UnmonitorAlbumWithoutFiles(albumId);
                }

                // Only now, so that RSS sync holds back the album until it is unmonitored.
                lock (_missingFromDisk)
                {
                    _missingFromDisk.Remove(albumId);
                }
            }
        }

        // No scan covers a root folder once it is removed, so what scans found missing from it would never be decided.
        public void Handle(ModelEvent<RootFolder> message)
        {
            if (message.Action != ModelAction.Deleted)
            {
                return;
            }

            var rootFolder = message.Model.Path;

            lock (_missingFromDisk)
            {
                foreach (var albumId in _missingFromDisk.Where(x => x.Value.Any(folder => rootFolder.PathEquals(folder) || rootFolder.IsParentPath(folder))).Select(x => x.Key).ToList())
                {
                    _missingFromDisk.Remove(albumId);
                }
            }
        }

        private void UnmonitorAlbumWithoutFiles(int albumId)
        {
            Album album;

            try
            {
                album = _albumService.GetAlbum(albumId);
            }
            catch (ModelNotFoundException)
            {
                // Deleted since its files went missing.
                return;
            }

            if (!album.Monitored || _mediaFileService.AlbumHasFiles(album.Id))
            {
                return;
            }

            _logger.Info("Unmonitoring album {0}, its files were deleted from disk", album);
            _albumService.SetMonitored(new List<int> { album.Id }, false);
        }
    }
}
