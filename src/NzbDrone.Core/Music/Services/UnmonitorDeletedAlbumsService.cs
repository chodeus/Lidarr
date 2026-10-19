using System;
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
        private readonly IRootFolderService _rootFolderService;
        private readonly IConfigService _configService;
        private readonly IDiskProvider _diskProvider;
        private readonly Logger _logger;

        // Folders that held the files disk scans found missing, by album, and whether a scan of the whole root folder each is
        // in has completed since. A scan can import the files again from anywhere in their root folder, so an album is only
        // decided once all its folders have been covered.
        private readonly Dictionary<int, Dictionary<string, bool>> _missingFromDisk = new Dictionary<int, Dictionary<string, bool>>();

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

            // Scans also drop the files in folders they skip, such as Extras, which are still on disk.
            if (message.Reason == DeleteMediaFileReason.MissingFromDisk && !_diskProvider.FileExists(trackFile.Path))
            {
                lock (_missingFromDisk)
                {
                    // A scan can still be reporting files from a root folder removed meanwhile, which no scan covers again.
                    // The removal deletes the root folder before it drops what was found missing, under this lock.
                    if (_rootFolderService.GetBestRootFolder(trackFile.Path) == null)
                    {
                        return;
                    }

                    if (!_missingFromDisk.TryGetValue(trackFile.AlbumId, out var folders))
                    {
                        folders = new Dictionary<string, bool>(PathEqualityComparer.Instance);
                        _missingFromDisk.Add(trackFile.AlbumId, folders);
                    }

                    folders[trackFile.Path.GetParentPath()] = false;
                }
            }

            // Lidarr deletes a file from disk before it drops it from the database, so one still on disk was not deleted.
            else if (message.Reason == DeleteMediaFileReason.Manual && !_diskProvider.FileExists(trackFile.Path))
            {
                if (UnmonitorAlbumWithoutFiles(trackFile.AlbumId))
                {
                    // Deleting its last file decided the album, so what scans found missing from it is not decided again.
                    lock (_missingFromDisk)
                    {
                        _missingFromDisk.Remove(trackFile.AlbumId);
                    }
                }
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
                // Root folders are often scanned one at a time, so what each scan covered counts until the rest is covered.
                foreach (var folders in _missingFromDisk.Values)
                {
                    foreach (var folder in folders.Keys.Where(folder => message.RootFolders.Any(root => root.PathEquals(folder) || root.IsParentPath(folder))).ToList())
                    {
                        folders[folder] = true;
                    }
                }

                albumIds = _missingFromDisk
                    .Where(x => message.MatchedAlbumIds.Contains(x.Key) || x.Value.Values.All(scanned => scanned))
                    .Select(x => x.Key)
                    .ToList();
            }

            foreach (var albumId in albumIds)
            {
                // Under the lock, so that removing the root folder either cancels the album first or waits for it.
                lock (_missingFromDisk)
                {
                    if (!_missingFromDisk.ContainsKey(albumId))
                    {
                        continue;
                    }

                    try
                    {
                        if (message.MatchedAlbumIds.Contains(albumId))
                        {
                            _logger.Debug("Not unmonitoring album {0}, the scan matched files to it that it did not import", albumId);
                        }
                        else
                        {
                            UnmonitorAlbumWithoutFiles(albumId);
                        }
                    }
                    catch (Exception e)
                    {
                        // Such as the album being deleted meanwhile. The next scan decides it again.
                        _logger.Warn(e, "Couldn't decide whether to unmonitor album {0}", albumId);
                        continue;
                    }

                    // Only now, so that RSS sync holds back the album until it is unmonitored.
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
                foreach (var albumId in _missingFromDisk.Where(x => x.Value.Keys.Any(folder => rootFolder.PathEquals(folder) || rootFolder.IsParentPath(folder))).Select(x => x.Key).ToList())
                {
                    _missingFromDisk.Remove(albumId);
                }
            }
        }

        // Whether the album is decided: deleted, or without files and so unmonitored.
        private bool UnmonitorAlbumWithoutFiles(int albumId)
        {
            Album album;

            try
            {
                album = _albumService.GetAlbum(albumId);
            }
            catch (ModelNotFoundException)
            {
                // Deleted since its files went missing.
                return true;
            }

            if (_mediaFileService.AlbumHasFiles(album.Id))
            {
                return false;
            }

            if (album.Monitored)
            {
                _logger.Info("Unmonitoring album {0}, its files were deleted from disk", album);
                _albumService.SetMonitored(new List<int> { album.Id }, false);
            }

            return true;
        }
    }
}
