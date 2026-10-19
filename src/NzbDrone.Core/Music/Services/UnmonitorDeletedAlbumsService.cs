using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.Music
{
    public interface IUnmonitorDeletedAlbumsService
    {
        // Whether a disk scan found the album's files missing, and a scan of their root folder is yet to complete.
        bool IsMissingFromDisk(int albumId);
    }

    public class UnmonitorDeletedAlbumsService : IUnmonitorDeletedAlbumsService,
                                                 IHandle<TrackFileDeletedEvent>,
                                                 IHandle<DiskScanCompletedEvent>
    {
        private readonly IAlbumService _albumService;
        private readonly IMediaFileService _mediaFileService;
        private readonly IRootFolderService _rootFolderService;
        private readonly IConfigService _configService;
        private readonly IDiskProvider _diskProvider;
        private readonly Logger _logger;

        // Folders that held the files disk scans found missing, by album. A scan can import the files again from
        // anywhere in their root folder, so an album is only unmonitored once a scan of that whole root folder completes.
        private readonly Dictionary<int, HashSet<string>> _missingFromDisk = new Dictionary<int, HashSet<string>>();

        public UnmonitorDeletedAlbumsService(IAlbumService albumService,
                                             IMediaFileService mediaFileService,
                                             IRootFolderService rootFolderService,
                                             IConfigService configService,
                                             IDiskProvider diskProvider,
                                             Logger logger)
        {
            _albumService = albumService;
            _mediaFileService = mediaFileService;
            _rootFolderService = rootFolderService;
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
            else if (message.Reason == DeleteMediaFileReason.Manual && WasDeletedFromDisk(trackFile))
            {
                UnmonitorAlbumsWithoutFiles(new List<int> { trackFile.AlbumId });
            }
        }

        // Removing a root folder drops its files from the database and leaves them where they are, so only a file gone
        // from a root folder that is still there was deleted. A root folder on offline storage is missing or empty, and
        // Lidarr refuses to delete files from one.
        private bool WasDeletedFromDisk(TrackFile trackFile)
        {
            var rootFolder = _rootFolderService.GetBestRootFolder(trackFile.Path);

            return rootFolder != null &&
                   _diskProvider.FolderExists(rootFolder.Path) &&
                   !_diskProvider.FolderEmpty(rootFolder.Path) &&
                   !_diskProvider.FileExists(trackFile.Path);
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

            List<int> albumIds;

            lock (_missingFromDisk)
            {
                albumIds = _missingFromDisk
                    .Where(x => x.Value.All(folder => message.RootFolders.Any(root => root.PathEquals(folder) || root.IsParentPath(folder))))
                    .Select(x => x.Key)
                    .ToList();

                albumIds.ForEach(id => _missingFromDisk.Remove(id));
            }

            // Files the scan matched to an album but did not import, such as files moved to where they no longer match
            // well enough, may still be that album's.
            foreach (var albumId in albumIds.Where(message.MatchedAlbumIds.Contains))
            {
                _logger.Debug("Not unmonitoring album {0}, the scan matched files to it that it did not import", albumId);
            }

            UnmonitorAlbumsWithoutFiles(albumIds.Where(id => !message.MatchedAlbumIds.Contains(id)).ToList());
        }

        private void UnmonitorAlbumsWithoutFiles(List<int> albumIds)
        {
            foreach (var albumId in albumIds)
            {
                Album album;

                try
                {
                    album = _albumService.GetAlbum(albumId);
                }
                catch (ModelNotFoundException)
                {
                    // Deleted since its files went missing.
                    continue;
                }

                if (!album.Monitored || _mediaFileService.AlbumHasFiles(album.Id))
                {
                    continue;
                }

                _logger.Info("Unmonitoring album {0}, its files were deleted from disk", album);
                _albumService.SetMonitored(new List<int> { album.Id }, false);
            }
        }
    }
}
