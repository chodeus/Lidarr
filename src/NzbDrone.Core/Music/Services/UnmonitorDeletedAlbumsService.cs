using System;
using System.Collections.Generic;
using System.IO;
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
                                                 IHandle<TrackFilesDeletingEvent>,
                                                 IHandle<TrackFileDeletedEvent>,
                                                 IHandle<DiskScanCompletedEvent>,
                                                 IHandle<ModelEvent<RootFolder>>
    {
        private readonly IAlbumService _albumService;
        private readonly IAlbumFileQuery _albumFileQuery;
        private readonly IRootFolderService _rootFolderService;
        private readonly IConfigService _configService;
        private readonly IDiskProvider _diskProvider;
        private readonly Logger _logger;

        // Albums disk scans dropped files of, until a scan decides them.
        private readonly Dictionary<int, DroppedFiles> _missingFromDisk = new Dictionary<int, DroppedFiles>();

        public UnmonitorDeletedAlbumsService(IAlbumService albumService,
                                             IAlbumFileQuery albumFileQuery,
                                             IRootFolderService rootFolderService,
                                             IConfigService configService,
                                             IDiskProvider diskProvider,
                                             Logger logger)
        {
            _albumService = albumService;
            _albumFileQuery = albumFileQuery;
            _rootFolderService = rootFolderService;
            _configService = configService;
            _diskProvider = diskProvider;
            _logger = logger;
        }

        public bool IsMissingFromDisk(int albumId)
        {
            lock (_missingFromDisk)
            {
                return _missingFromDisk.TryGetValue(albumId, out var dropped) && dropped.Folders.Any();
            }
        }

        // Before the scan drops the files from the database, so that RSS sync holds back their albums from then on.
        public void Handle(TrackFilesDeletingEvent message)
        {
            if (message.Reason != DeleteMediaFileReason.MissingFromDisk || !_configService.AutoUnmonitorPreviouslyDownloadedTracks)
            {
                return;
            }

            // Outside the lock, which RSS sync waits on.
            var listings = new Dictionary<string, HashSet<string>>(PathEqualityComparer.Instance);
            var files = message.TrackFiles
                .Where(f => f.AlbumId > 0)
                .Select(f => (File: f, RootFolder: _rootFolderService.GetBestRootFolder(f.Path)))
                .Where(x => x.RootFolder != null)
                .Select(x => (x.File, Gone: IsGoneFromDisk(x.File.Path, x.RootFolder.Path, listings)))
                .ToList();

            lock (_missingFromDisk)
            {
                foreach (var (file, gone) in files)
                {
                    // A scan can still be cleaning a root folder removed meanwhile, which no scan covers again. The removal
                    // deletes the root folder before it drops what was found missing, under this lock.
                    if (_rootFolderService.GetBestRootFolder(file.Path) == null)
                    {
                        continue;
                    }

                    if (!_missingFromDisk.TryGetValue(file.AlbumId, out var dropped))
                    {
                        dropped = new DroppedFiles();
                        _missingFromDisk.Add(file.AlbumId, dropped);
                    }

                    if (gone)
                    {
                        dropped.Folders[file.Path.GetParentPath()] = false;
                    }
                    else
                    {
                        dropped.StillOnDisk = true;
                    }
                }
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

            // Lidarr deletes a file from disk before it drops it from the database, so one still on disk was not deleted.
            if (message.Reason == DeleteMediaFileReason.Manual && !_diskProvider.FileExists(trackFile.Path))
            {
                lock (_missingFromDisk)
                {
                    // Files a scan found missing may be elsewhere in their root folder, so a scan of it decides the album.
                    if (!_missingFromDisk.ContainsKey(trackFile.AlbumId))
                    {
                        UnmonitorAlbumWithoutFiles(trackFile.AlbumId);
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
                foreach (var dropped in _missingFromDisk.Values)
                {
                    foreach (var folder in dropped.Folders.Keys.Where(folder => message.RootFolders.Any(root => root.PathEquals(folder) || root.IsParentPath(folder))).ToList())
                    {
                        dropped.Folders[folder] = true;
                    }
                }

                albumIds = _missingFromDisk
                    .Where(x => message.MatchedAlbumIds.Contains(x.Key) || x.Value.Folders.Values.All(scanned => scanned))
                    .Select(x => x.Key)
                    .ToList();
            }

            foreach (var albumId in albumIds)
            {
                // Under the lock, so that removing the root folder either cancels the album first or waits for it.
                lock (_missingFromDisk)
                {
                    if (!_missingFromDisk.TryGetValue(albumId, out var dropped))
                    {
                        continue;
                    }

                    try
                    {
                        if (message.MatchedAlbumIds.Contains(albumId))
                        {
                            _logger.Debug("Not unmonitoring album {0}, the scan matched files to it", albumId);
                        }
                        else if (dropped.StillOnDisk)
                        {
                            _logger.Debug("Not unmonitoring album {0}, the scan dropped files of it that are still on disk", albumId);
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
                foreach (var albumId in _missingFromDisk.Where(x => x.Value.Folders.Keys.Any(folder => rootFolder.PathEquals(folder) || rootFolder.IsParentPath(folder))).Select(x => x.Key).ToList())
                {
                    _missingFromDisk.Remove(albumId);
                }
            }
        }

        // Whether the file is gone, rather than out of reach: scans skip the folders they cannot read, and FileExists cannot
        // tell a file in one from a deleted file. Gone means the nearest folder above it that is still there lists without it.
        private bool IsGoneFromDisk(string path, string rootFolder, Dictionary<string, HashSet<string>> listings)
        {
            if (_diskProvider.FileExists(path))
            {
                return false;
            }

            var entry = path;
            var folder = path.GetParentPath();

            while (!_diskProvider.FolderExists(folder))
            {
                // Not even the root folder is there, such as storage gone offline.
                if (folder.PathEquals(rootFolder) || !rootFolder.IsParentPath(folder))
                {
                    return false;
                }

                entry = folder;
                folder = folder.GetParentPath();
            }

            if (!listings.TryGetValue(folder, out var entries))
            {
                try
                {
                    // GetDirectories fails on a folder Lidarr cannot list, where GetFiles would return nothing.
                    entries = new HashSet<string>(_diskProvider.GetDirectories(folder).Concat(_diskProvider.GetFiles(folder, false)), PathEqualityComparer.Instance);
                }
                catch (Exception e) when (e is UnauthorizedAccessException || e is IOException)
                {
                    _logger.Debug(e, "Couldn't list {0}", folder);
                    entries = null;
                }

                listings[folder] = entries;
            }

            // Listed but not found, such as in a folder Lidarr can list but not enter.
            return entries != null && !entries.Contains(entry);
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

            if (album.Monitored && !_albumFileQuery.AlbumHasFiles(album.Id))
            {
                _logger.Info("Unmonitoring album {0}, its files were deleted from disk", album);
                _albumService.SetMonitored(new List<int> { album.Id }, false);
            }
        }

        private class DroppedFiles
        {
            // Folders that held its files found gone from disk, and whether a scan of the whole root folder each is in has
            // completed since. A scan can import the files again from anywhere in their root folder, so the album is only
            // decided once all its folders have been covered.
            public Dictionary<string, bool> Folders { get; } = new Dictionary<string, bool>(PathEqualityComparer.Instance);

            // Whether any of its files were still on disk, or out of reach, such as in folders scans skip like Extras.
            public bool StillOnDisk { get; set; }
        }
    }
}
