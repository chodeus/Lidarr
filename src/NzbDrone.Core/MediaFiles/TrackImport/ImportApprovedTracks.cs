using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Extras;
using NzbDrone.Core.History;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Music;
using NzbDrone.Core.Music.Commands;
using NzbDrone.Core.Music.Events;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.MediaFiles.TrackImport
{
    public interface IImportApprovedTracks
    {
        List<ImportResult> Import(List<ImportDecision<LocalTrack>> decisions, bool replaceExisting, DownloadClientItem downloadClientItem = null, ImportMode importMode = ImportMode.Auto);
    }

    public class ImportApprovedTracks : IImportApprovedTracks
    {
        // Hidden, so a disk scan skips the files waiting here (DiskScanService.ExcludedSubFoldersRegex)
        private const string ReplacedFolderName = ".lidarr-replaced";

        private readonly IUpgradeMediaFiles _trackFileUpgrader;
        private readonly IMediaFileService _mediaFileService;
        private readonly IAudioTagService _audioTagService;
        private readonly ITrackService _trackService;
        private readonly IArtistService _artistService;
        private readonly IAddArtistService _addArtistService;
        private readonly IAlbumService _albumService;
        private readonly IRefreshAlbumService _refreshAlbumService;
        private readonly IRootFolderService _rootFolderService;
        private readonly IRecycleBinProvider _recycleBinProvider;
        private readonly IExtraService _extraService;
        private readonly IDiskProvider _diskProvider;
        private readonly IHistoryService _historyService;
        private readonly IReleaseService _releaseService;
        private readonly IEventAggregator _eventAggregator;
        private readonly IManageCommandQueue _commandQueueManager;
        private readonly Logger _logger;

        public ImportApprovedTracks(IUpgradeMediaFiles trackFileUpgrader,
                                    IMediaFileService mediaFileService,
                                    IAudioTagService audioTagService,
                                    ITrackService trackService,
                                    IArtistService artistService,
                                    IAddArtistService addArtistService,
                                    IAlbumService albumService,
                                    IRefreshAlbumService refreshAlbumService,
                                    IRootFolderService rootFolderService,
                                    IRecycleBinProvider recycleBinProvider,
                                    IExtraService extraService,
                                    IDiskProvider diskProvider,
                                    IHistoryService historyService,
                                    IReleaseService releaseService,
                                    IEventAggregator eventAggregator,
                                    IManageCommandQueue commandQueueManager,
                                    Logger logger)
        {
            _trackFileUpgrader = trackFileUpgrader;
            _mediaFileService = mediaFileService;
            _audioTagService = audioTagService;
            _trackService = trackService;
            _artistService = artistService;
            _addArtistService = addArtistService;
            _albumService = albumService;
            _refreshAlbumService = refreshAlbumService;
            _rootFolderService = rootFolderService;
            _recycleBinProvider = recycleBinProvider;
            _extraService = extraService;
            _diskProvider = diskProvider;
            _historyService = historyService;
            _releaseService = releaseService;
            _eventAggregator = eventAggregator;
            _commandQueueManager = commandQueueManager;
            _logger = logger;
        }

        public List<ImportResult> Import(List<ImportDecision<LocalTrack>> decisions, bool replaceExisting, DownloadClientItem downloadClientItem = null, ImportMode importMode = ImportMode.Auto)
        {
            var importResults = new List<ImportResult>();
            var allImportedTrackFiles = new List<TrackFile>();
            var allOldTrackFiles = new List<TrackFile>();
            var addedArtists = new List<Artist>();

            var albumDecisions = decisions.Where(e => e.Item.Album != null && e.Approved)
                .GroupBy(e => e.Item.Album.ForeignAlbumId).ToList();

            var replacements = new List<ReplacedAlbum>();
            var iDecision = 1;
            foreach (var albumDecision in albumDecisions)
            {
                _logger.ProgressInfo($"Importing album {iDecision++}/{albumDecisions.Count}");

                var decisionList = albumDecision.ToList();

                var artist = EnsureArtistAdded(decisionList, addedArtists);

                if (artist == null)
                {
                    // failed to add the artist, carry on with next album
                    continue;
                }

                var album = EnsureAlbumAdded(decisionList);

                if (album == null)
                {
                    // failed to add the album, carry on with next one
                    continue;
                }

                if (replaceExisting)
                {
                    replacements.Add(MoveExistingTrackFilesAside(artist, album, decisionList));
                }

                // set the correct release to be monitored before importing the new files
                var newRelease = albumDecision.First().Item.Release;
                _logger.Debug("Updating release to {0} [{1} tracks]", newRelease, newRelease.TrackCount);
                album.AlbumReleases = _releaseService.SetMonitored(newRelease);

                // Publish album edited event.
                // Deliberately don't put in the old album since we don't want to trigger an ArtistScan.
                _eventAggregator.PublishEvent(new AlbumEditedEvent(album, album));
            }

            var qualifiedImports = decisions
                .Where(decision => decision.Approved)
                .GroupBy(decision => decision.Item.Artist.Id)
                .SelectMany(group => group
                    .OrderByDescending(decision => decision.Item.Quality, new QualityModelComparer(group.First().Item.Artist.QualityProfile))
                    .ThenByDescending(decision => decision.Item.Size))
                .ToList();

            _logger.ProgressInfo($"Importing {qualifiedImports.Count} tracks");
            _logger.Debug($"Importing {qualifiedImports.Count} files. replaceExisting: {replaceExisting}");

            var filesToAdd = new List<TrackFile>(qualifiedImports.Count);
            var albumReleasesDict = new Dictionary<int, List<AlbumRelease>>(albumDecisions.Count);
            var trackImportedEvents = new List<TrackImportedEvent>(qualifiedImports.Count);

            foreach (var importDecision in qualifiedImports.OrderBy(e => e.Item.Tracks.Select(track => track.AbsoluteTrackNumber).MinOrDefault())
                                                           .ThenByDescending(e => e.Item.Size))
            {
                var localTrack = importDecision.Item;
                var oldFiles = new List<TrackFile>();

                try
                {
                    // check if already imported
                    if (importResults.SelectMany(r => r.ImportDecision.Item.Tracks)
                                         .Select(e => e.Id)
                                         .Intersect(localTrack.Tracks.Select(e => e.Id))
                                         .Any())
                    {
                        importResults.Add(new ImportResult(importDecision, "Track has already been imported"));
                        continue;
                    }

                    // cache album releases and set artist to speed up firing the TrackImported events
                    // (otherwise they'll be retrieved from the DB for each track)
                    if (!albumReleasesDict.ContainsKey(localTrack.Album.Id))
                    {
                        albumReleasesDict.Add(localTrack.Album.Id, localTrack.Album.AlbumReleases.Value);
                    }

                    if (!localTrack.Album.AlbumReleases.IsLoaded)
                    {
                        localTrack.Album.AlbumReleases = albumReleasesDict[localTrack.Album.Id];
                    }

                    localTrack.Album.Artist = localTrack.Artist;

                    foreach (var track in localTrack.Tracks)
                    {
                        track.Artist = localTrack.Artist;
                        track.AlbumRelease = localTrack.Release;
                        track.Album = localTrack.Album;
                    }

                    var trackFile = new TrackFile
                    {
                        Path = localTrack.Path.CleanFilePath(),
                        Size = localTrack.Size,
                        Modified = localTrack.Modified,
                        DateAdded = DateTime.UtcNow,
                        ReleaseGroup = localTrack.ReleaseGroup,
                        Quality = localTrack.Quality,
                        MediaInfo = localTrack.FileTrackInfo.MediaInfo,
                        AlbumId = localTrack.Album.Id,
                        Artist = localTrack.Artist,
                        Album = localTrack.Album,
                        Tracks = localTrack.Tracks
                    };

                    if (downloadClientItem?.DownloadId.IsNotNullOrWhiteSpace() == true)
                    {
                        var grabHistory = _historyService.FindByDownloadId(downloadClientItem.DownloadId)
                            .OrderByDescending(h => h.Date)
                            .FirstOrDefault(h => h.EventType == EntityHistoryEventType.Grabbed);

                        if (Enum.TryParse(grabHistory?.Data.GetValueOrDefault("indexerFlags"), true, out IndexerFlags flags))
                        {
                            trackFile.IndexerFlags = flags;
                        }
                    }
                    else
                    {
                        trackFile.IndexerFlags = localTrack.IndexerFlags;
                    }

                    bool copyOnly;
                    switch (importMode)
                    {
                        default:
                        case ImportMode.Auto:
                            copyOnly = downloadClientItem is { CanMoveFiles: false };
                            break;
                        case ImportMode.Move:
                            copyOnly = false;
                            break;
                        case ImportMode.Copy:
                            copyOnly = true;
                            break;
                    }

                    if (!localTrack.ExistingFile)
                    {
                        trackFile.SceneName = localTrack.SceneName;
                        trackFile.OriginalFilePath = GetOriginalFilePath(downloadClientItem, localTrack);

                        var moveResult = _trackFileUpgrader.UpgradeTrackFile(trackFile, localTrack, copyOnly);
                        oldFiles = moveResult.OldFiles;
                    }
                    else
                    {
                        // Delete existing files from the DB mapped to this path
                        var previousFile = _mediaFileService.GetFileWithPath(trackFile.Path);

                        if (previousFile != null)
                        {
                            _mediaFileService.Delete(previousFile, DeleteMediaFileReason.ManualOverride);
                        }

                        try
                        {
                            _audioTagService.WriteTags(trackFile, false);
                        }
                        catch (InvalidOperationException ex)
                        {
                            _logger.Warn(ex, "Failed to write tags for {0}: {1}. Try refreshing the artist to fix missing information.", trackFile.Path, ex.Message);
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(ex, "Unexpected error writing tags for existing track file {0}", trackFile.Path);
                        }
                    }

                    filesToAdd.Add(trackFile);
                    importResults.Add(new ImportResult(importDecision));

                    if (!localTrack.ExistingFile)
                    {
                        _extraService.ImportTrack(localTrack, trackFile, copyOnly);
                    }

                    allImportedTrackFiles.Add(trackFile);
                    allOldTrackFiles.AddRange(oldFiles);

                    // create all the import events here, but we can't publish until the trackfiles have been
                    // inserted and ids created
                    trackImportedEvents.Add(new TrackImportedEvent(localTrack, trackFile, oldFiles, !localTrack.ExistingFile, downloadClientItem));
                }
                catch (RootFolderNotFoundException e)
                {
                    _logger.Warn(e, "Couldn't import track " + localTrack);
                    _eventAggregator.PublishEvent(new TrackImportFailedEvent(e, localTrack, !localTrack.ExistingFile, downloadClientItem));

                    importResults.Add(new ImportResult(importDecision, "Failed to import track, Root folder missing."));
                }
                catch (DestinationAlreadyExistsException e)
                {
                    _logger.Warn(e, "Couldn't import track " + localTrack);
                    importResults.Add(new ImportResult(importDecision, "Failed to import track, Destination already exists."));
                }
                catch (UnauthorizedAccessException e)
                {
                    _logger.Warn(e, "Couldn't import track " + localTrack);
                    _eventAggregator.PublishEvent(new TrackImportFailedEvent(e, localTrack, !localTrack.ExistingFile, downloadClientItem));

                    importResults.Add(new ImportResult(importDecision, "Failed to import track, Permissions error"));
                }
                catch (RecycleBinException e)
                {
                    _logger.Warn(e, "Couldn't import track " + localTrack);
                    _eventAggregator.PublishEvent(new TrackImportFailedEvent(e, localTrack, !localTrack.ExistingFile, downloadClientItem));

                    importResults.Add(new ImportResult(importDecision, "Failed to import track, unable to move existing file to the Recycle Bin."));
                }
                catch (Exception e)
                {
                    _logger.Warn(e, "Couldn't import track " + localTrack);
                    importResults.Add(new ImportResult(importDecision, "Failed to import track"));
                }
            }

            foreach (var replacement in replacements)
            {
                FinishReplacing(replacement, importResults);
            }

            var watch = new System.Diagnostics.Stopwatch();
            watch.Start();
            _mediaFileService.AddMany(filesToAdd);
            _logger.Debug($"Inserted new trackfiles in {watch.ElapsedMilliseconds}ms");
            filesToAdd.ForEach(f => f.Tracks.Value.ForEach(t => t.TrackFileId = f.Id));
            _trackService.SetFileIds(filesToAdd.SelectMany(x => x.Tracks.Value).ToList());
            _logger.Debug($"TrackFileIds updated, total {watch.ElapsedMilliseconds}ms");

            // now that trackfiles have been inserted and ids generated, publish the import events
            foreach (var trackImportedEvent in trackImportedEvents)
            {
                _eventAggregator.PublishEvent(trackImportedEvent);
            }

            var albumImports = importResults.Where(e => e.ImportDecision.Item.Album != null)
                .GroupBy(e => e.ImportDecision.Item.Album.Id).ToList();

            foreach (var albumImport in albumImports)
            {
                var release = albumImport.First().ImportDecision.Item.Release;
                var album = _albumService.GetAlbum(albumImport.First().ImportDecision.Item.Album.Id);
                var artist = albumImport.First().ImportDecision.Item.Artist;

                if (albumImport.Where(e => e.Errors.Count == 0).ToList().Count > 0 && artist != null && album != null)
                {
                    _eventAggregator.PublishEvent(new AlbumImportedEvent(
                        artist,
                        album,
                        release,
                        allImportedTrackFiles.Where(s => s.AlbumId == album.Id).ToList(),
                        allOldTrackFiles.Where(s => s.AlbumId == album.Id).ToList(),
                        replaceExisting,
                        downloadClientItem));
                }
            }

            // Adding all the rejected decisions
            importResults.AddRange(decisions.Where(c => !c.Approved)
                                            .Select(d => new ImportResult(d, d.Rejections.Select(r => r.Reason).ToArray())));

            // Refresh any artists we added
            if (addedArtists.Any())
            {
                _commandQueueManager.Push(new BulkRefreshArtistCommand(addedArtists.Select(x => x.Id).ToList(), true));
            }

            return importResults;
        }

        private Artist EnsureArtistAdded(List<ImportDecision<LocalTrack>> decisions, List<Artist> addedArtists)
        {
            var artist = decisions.First().Item.Artist;

            if (artist.Id == 0)
            {
                var dbArtist = _artistService.FindById(artist.ForeignArtistId);

                if (dbArtist == null)
                {
                    _logger.Debug($"Adding remote artist {artist}");
                    var rootFolder = _rootFolderService.GetBestRootFolder(decisions.First().Item.Path);

                    artist.RootFolderPath = rootFolder.Path;
                    artist.MetadataProfileId = rootFolder.DefaultMetadataProfileId;
                    artist.QualityProfileId = rootFolder.DefaultQualityProfileId;
                    artist.Monitored = rootFolder.DefaultMonitorOption != MonitorTypes.None;
                    artist.MonitorNewItems = rootFolder.DefaultNewItemMonitorOption;
                    artist.Tags = rootFolder.DefaultTags;
                    artist.AddOptions = new AddArtistOptions
                    {
                        SearchForMissingAlbums = false,
                        Monitored = artist.Monitored,
                        Monitor = rootFolder.DefaultMonitorOption
                    };

                    try
                    {
                        dbArtist = _addArtistService.AddArtist(artist, false);
                        addedArtists.Add(dbArtist);
                    }
                    catch (Exception e)
                    {
                        _logger.Error(e, "Failed to add artist {0}", artist);
                        foreach (var decision in decisions)
                        {
                            decision.Reject(new Rejection("Failed to add missing artist", RejectionType.Temporary));
                        }

                        return null;
                    }
                }

                // Put in the newly loaded artist
                foreach (var decision in decisions)
                {
                    decision.Item.Artist = dbArtist;
                    decision.Item.Album.Artist = dbArtist;
                    decision.Item.Album.ArtistMetadataId = dbArtist.ArtistMetadataId;
                }

                artist = dbArtist;
            }

            return artist;
        }

        private Album EnsureAlbumAdded(List<ImportDecision<LocalTrack>> decisions)
        {
            var album = decisions.First().Item.Album;

            if (album.Id == 0)
            {
                var dbAlbum = _albumService.FindById(album.ForeignAlbumId);

                if (dbAlbum == null)
                {
                    _logger.Debug($"Adding remote album {album}");
                    try
                    {
                        album.Monitored = album.Artist.Value.Monitored;
                        album.Added = DateTime.UtcNow;
                        _albumService.InsertMany(new List<Album> { album });
                        _refreshAlbumService.RefreshAlbumInfo(album, new List<Album> { album }, false);
                        dbAlbum = _albumService.FindById(album.ForeignAlbumId);
                    }
                    catch (Exception e)
                    {
                        _logger.Error(e, "Failed to add album {0}", album);
                        RejectAlbum(decisions);

                        return null;
                    }
                }

                var release = dbAlbum.AlbumReleases.Value.ExclusiveOrDefault(x => x.ForeignReleaseId == decisions.First().Item.Release.ForeignReleaseId);
                if (release == null)
                {
                    RejectAlbum(decisions);
                    return null;
                }

                // Populate the new DB album
                foreach (var decision in decisions)
                {
                    decision.Item.Album = dbAlbum;
                    decision.Item.Release = release;
                    var trackIds = decision.Item.Tracks.Select(x => x.ForeignTrackId).ToList();
                    decision.Item.Tracks = release.Tracks.Value.Where(x => trackIds.Contains(x.ForeignTrackId)).ToList();
                }
            }

            return album;
        }

        private void RejectAlbum(List<ImportDecision<LocalTrack>> decisions)
        {
            foreach (var decision in decisions)
            {
                decision.Reject(new Rejection("Failed to add missing album", RejectionType.Temporary));
            }
        }

        // Aside rather than recycled, so the album keeps its release and files when nothing of the new one imports
        private ReplacedAlbum MoveExistingTrackFilesAside(Artist artist, Album album, List<ImportDecision<LocalTrack>> decisions)
        {
            var replacement = new ReplacedAlbum
            {
                Artist = artist,
                Album = album,
                PreviousRelease = album.AlbumReleases?.Value?.SingleOrDefault(r => r.Monitored)
            };

            var previousFiles = _mediaFileService.GetFilesByAlbum(album.Id);

            _logger.Debug($"Moving {previousFiles.Count} existing files for {album} aside");

            try
            {
                foreach (var previousFile in previousFiles)
                {
                    var file = new ReplacedFile { TrackFile = previousFile, OriginalPath = previousFile.Path };
                    replacement.Files.Add(file);

                    if (!_diskProvider.FileExists(previousFile.Path))
                    {
                        continue;
                    }

                    var asideFolder = Path.Combine(_diskProvider.GetParentFolder(previousFile.Path), ReplacedFolderName);
                    var asidePath = FreePath(asideFolder, Path.GetFileName(previousFile.Path));

                    _diskProvider.CreateFolder(asideFolder);
                    _diskProvider.MoveFile(previousFile.Path, asidePath);

                    file.AsidePath = asidePath;
                    previousFile.Path = asidePath;
                }

                // The rows follow the files, so an upgrade of the same release still finds them and a crash leaves them valid
                _mediaFileService.Update(previousFiles);

                // Import specs loaded these tracks' files before the move, as copies at the old paths
                var moved = previousFiles.ToDictionary(f => f.Id);

                foreach (var track in decisions.SelectMany(d => d.Item.Tracks).Where(t => moved.ContainsKey(t.TrackFileId)))
                {
                    track.TrackFile = moved[track.TrackFileId];
                }
            }
            catch
            {
                MoveBack(replacement);
                throw;
            }

            return replacement;
        }

        // A file an interrupted import left here keeps its name; this one gets a suffix, as in the recycle bin
        private string FreePath(string folder, string fileName)
        {
            var path = Path.Combine(folder, fileName);

            for (var index = 2; _diskProvider.FileExists(path); index++)
            {
                path = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(fileName)}_{index}{Path.GetExtension(fileName)}");
            }

            return path;
        }

        private void FinishReplacing(ReplacedAlbum replacement, List<ImportResult> importResults)
        {
            if (importResults.Any(r => r.Result == ImportResultType.Imported && r.ImportDecision.Item.Album?.Id == replacement.Album.Id))
            {
                RecycleReplaced(replacement);
                return;
            }

            _logger.Warn("No track of {0} imported, keeping its previous release and files", replacement.Album);

            _mediaFileService.Update(MoveBack(replacement));

            if (replacement.PreviousRelease != null)
            {
                replacement.Album.AlbumReleases = _releaseService.SetMonitored(replacement.PreviousRelease);
                _eventAggregator.PublishEvent(new AlbumEditedEvent(replacement.Album, replacement.Album));
            }
        }

        private void RecycleReplaced(ReplacedAlbum replacement)
        {
            var rootFolder = _diskProvider.GetParentFolder(replacement.Artist.Path);
            var remaining = _mediaFileService.GetFilesByAlbum(replacement.Album.Id).Select(f => f.Id).ToHashSet();

            foreach (var file in replacement.Files)
            {
                if (file.AsidePath != null && _diskProvider.FileExists(file.AsidePath))
                {
                    var subfolder = rootFolder.GetRelativePath(_diskProvider.GetParentFolder(file.OriginalPath));

                    try
                    {
                        _logger.Debug("Removing existing track file: {0}", file.OriginalPath);
                        _recycleBinProvider.DeleteFile(file.AsidePath, subfolder);
                    }
                    catch (Exception e)
                    {
                        _logger.Warn(e, "Couldn't recycle replaced track file, left at {0}", file.AsidePath);
                    }
                }

                // An upgrade of the same release has already removed its own files
                if (remaining.Contains(file.TrackFile.Id))
                {
                    file.TrackFile.Path = file.OriginalPath;
                    _mediaFileService.Delete(file.TrackFile, DeleteMediaFileReason.Upgrade);
                }
            }

            DeleteEmptyReplacedFolders(replacement);
        }

        private List<TrackFile> MoveBack(ReplacedAlbum replacement)
        {
            var restored = new List<TrackFile>();

            foreach (var file in replacement.Files.Where(f => f.AsidePath != null && _diskProvider.FileExists(f.AsidePath)))
            {
                try
                {
                    _diskProvider.MoveFile(file.AsidePath, file.OriginalPath);
                    file.TrackFile.Path = file.OriginalPath;
                    restored.Add(file.TrackFile);
                }
                catch (Exception e)
                {
                    _logger.Warn(e, "Couldn't move {0} back to {1}", file.AsidePath, file.OriginalPath);
                }
            }

            DeleteEmptyReplacedFolders(replacement);

            return restored;
        }

        private void DeleteEmptyReplacedFolders(ReplacedAlbum replacement)
        {
            foreach (var folder in replacement.Files.Where(f => f.AsidePath != null).Select(f => _diskProvider.GetParentFolder(f.AsidePath)).Distinct())
            {
                if (_diskProvider.FolderExists(folder) && _diskProvider.FolderEmpty(folder))
                {
                    _diskProvider.DeleteFolder(folder, false);
                }
            }
        }

        private string GetOriginalFilePath(DownloadClientItem downloadClientItem, LocalTrack localTrack)
        {
            var path = localTrack.Path;

            if (downloadClientItem != null && !downloadClientItem.OutputPath.IsEmpty)
            {
                var outputDirectory = downloadClientItem.OutputPath.Directory.ToString();

                if (outputDirectory.IsParentPath(path))
                {
                    return outputDirectory.GetRelativePath(path);
                }
            }

            var folderEpisodeInfo = localTrack.FolderAlbumInfo;

            if (folderEpisodeInfo != null)
            {
                var folderPath = path.GetAncestorPath(folderEpisodeInfo.ReleaseTitle);

                if (folderPath != null)
                {
                    return folderPath.GetParentPath().GetRelativePath(path);
                }
            }

            var parentPath = path.GetParentPath();
            var grandparentPath = parentPath.GetParentPath();

            if (grandparentPath != null)
            {
                return grandparentPath.GetRelativePath(path);
            }

            return Path.GetFileName(path);
        }

        private class ReplacedAlbum
        {
            public Artist Artist { get; set; }
            public Album Album { get; set; }
            public AlbumRelease PreviousRelease { get; set; }
            public List<ReplacedFile> Files { get; } = new List<ReplacedFile>();
        }

        private class ReplacedFile
        {
            public TrackFile TrackFile { get; set; }
            public string OriginalPath { get; set; }
            public string AsidePath { get; set; }
        }
    }
}
