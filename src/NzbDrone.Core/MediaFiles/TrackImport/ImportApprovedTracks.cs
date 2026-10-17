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
using NzbDrone.Core.Extras.Files;
using NzbDrone.Core.Extras.Lyrics;
using NzbDrone.Core.Extras.Metadata.Files;
using NzbDrone.Core.Extras.Others;
using NzbDrone.Core.History;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Music;
using NzbDrone.Core.Music.Commands;
using NzbDrone.Core.Music.Events;
using NzbDrone.Core.Parser;
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
        private const string BackupFolderName = ".lidarr-replaced";

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
        private readonly ILyricFileService _lyricFileService;
        private readonly IMetadataFileService _metadataFileService;
        private readonly IOtherExtraFileService _otherExtraFileService;
        private readonly IDiskProvider _diskProvider;
        private readonly IHistoryService _historyService;
        private readonly IReleaseService _releaseService;
        private readonly IEventAggregator _eventAggregator;
        private readonly IManageCommandQueue _commandQueueManager;
        private readonly Logger _logger;

        // Develop's constructor, kept for plugins built against it; without the extra file services a put-back album loses its extras, as in develop
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
            : this(trackFileUpgrader, mediaFileService, audioTagService, trackService, artistService, addArtistService, albumService, refreshAlbumService, rootFolderService, recycleBinProvider, extraService, diskProvider, historyService, releaseService, eventAggregator, commandQueueManager, null, null, null, logger)
        {
        }

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
                                    ILyricFileService lyricFileService,
                                    IMetadataFileService metadataFileService,
                                    IOtherExtraFileService otherExtraFileService,
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
            _lyricFileService = lyricFileService;
            _metadataFileService = metadataFileService;
            _otherExtraFileService = otherExtraFileService;
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
            var releaseSwitches = new List<(Album Album, AlbumRelease Release)>();
            var iDecision = 1;
            List<ImportDecision<LocalTrack>> qualifiedImports;

            try
            {
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

                    // An album added since identification has Id 0 here; the decisions now carry the database copy
                    album = decisionList.First().Item.Album;

                    if (replaceExisting)
                    {
                        replacements.Add(BackUpExistingTrackFiles(artist, album));
                        RemoveExistingTrackFiles(artist, album);
                    }

                    // The tracks carry their release through the import; the album switches once one of them has imported
                    releaseSwitches.Add((album, albumDecision.First().Item.Release));
                }

                qualifiedImports = decisions
                    .Where(decision => decision.Approved)
                    .GroupBy(decision => decision.Item.Artist.Id)
                    .SelectMany(group => group
                        .OrderByDescending(decision => decision.Item.Quality, new QualityModelComparer(group.First().Item.Artist.QualityProfile))
                        .ThenByDescending(decision => decision.Item.Size))
                    .ToList();
            }
            catch
            {
                // Adding albums and loading quality profiles read the database; nothing has imported yet, so every album moved aside goes back
                replacements.ForEach(TryRestore);
                throw;
            }

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

                    if (IsLinkedAndUnchanged(localTrack))
                    {
                        _logger.Debug("Skipping {0}, already linked to these tracks and unchanged on disk", localTrack.Path);
                        importResults.Add(new ImportResult(importDecision, "Track file is already linked to these tracks"));
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

            var watch = new System.Diagnostics.Stopwatch();
            watch.Start();

            try
            {
                _mediaFileService.AddMany(filesToAdd);
                _logger.Debug($"Inserted new trackfiles in {watch.ElapsedMilliseconds}ms");
                filesToAdd.ForEach(f => f.Tracks.Value.ForEach(t => t.TrackFileId = f.Id));
                _trackService.SetFileIds(filesToAdd.SelectMany(x => x.Tracks.Value).ToList());
            }
            catch
            {
                // Rows saved before the failure never linked; they go first, so the old files can take their paths back
                DeleteUnlinkedRows(filesToAdd);
                replacements.ForEach(TryRestore);
                throw;
            }

            _logger.Debug($"TrackFileIds updated, total {watch.ElapsedMilliseconds}ms");

            foreach (var (album, release) in releaseSwitches)
            {
                FinishAlbum(album, release, replacements.SingleOrDefault(r => r.Album.Id == album.Id), importResults, filesToAdd);
            }

            if (replaceExisting)
            {
                try
                {
                    RemoveOrphanedCopies(allImportedTrackFiles);
                }
                catch (Exception e)
                {
                    _logger.Warn(e, "Couldn't look for unmapped copies of the imported tracks");
                }
            }

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

        // A rescan that identifies an unmapped sibling pulls the album's linked files in too; re-importing them only recreates their rows
        private bool IsLinkedAndUnchanged(LocalTrack localTrack)
        {
            if (!localTrack.ExistingFile || localTrack.Tracks.Empty())
            {
                return false;
            }

            var existingFile = _mediaFileService.GetFileWithPath(localTrack.Path.CleanFilePath());

            return existingFile != null &&
                   existingFile.AlbumId == localTrack.Album.Id &&
                   existingFile.IsUnchanged(localTrack.Size, localTrack.Modified) &&
                   existingFile.Tracks?.Value != null &&
                   existingFile.Tracks.Value.Select(t => t.Id).ToHashSet().SetEquals(localTrack.Tracks.Select(t => t.Id));
        }

        // RemoveExistingTrackFiles skips unlinked copies (AlbumId 0); only remove one the import at least matches in quality
        private void RemoveOrphanedCopies(List<TrackFile> importedFiles)
        {
            foreach (var folder in importedFiles.GroupBy(f => Path.GetDirectoryName(f.Path)))
            {
                var artist = folder.First().Artist.Value;
                var qualityComparer = new QualityModelComparer(artist.QualityProfile);

                var orphans = _mediaFileService.GetFilesWithBasePath(folder.Key)
                    .Where(f => f.AlbumId == 0 &&
                                Path.GetDirectoryName(f.Path).PathEquals(folder.Key) &&
                                folder.All(i => !i.Path.PathEquals(f.Path)) &&
                                _diskProvider.FileExists(f.Path))
                    .ToList();

                foreach (var orphan in orphans)
                {
                    var tags = _audioTagService.ReadTags(orphan.Path);
                    var replacements = folder.Where(i => i.Tracks.Value.Any(t => IsSameTrack(tags, t))).ToList();

                    if (replacements.Empty())
                    {
                        continue;
                    }

                    // Group order counts, or a 16-bit import would replace a 24-bit copy in the same group
                    if (replacements.All(i => qualityComparer.Compare(i.Quality, orphan.Quality, true) < 0))
                    {
                        _logger.Debug("Keeping unmapped copy {0}, its quality {1} beats the import", orphan.Path, orphan.Quality);
                        continue;
                    }

                    _logger.Info("Removing unmapped copy of an imported track: {0}", orphan.Path);

                    try
                    {
                        var subfolder = _diskProvider.GetParentFolder(artist.Path).GetRelativePath(_diskProvider.GetParentFolder(orphan.Path));
                        _recycleBinProvider.DeleteFile(orphan.Path, subfolder);
                    }
                    catch (Exception e)
                    {
                        _logger.Warn(e, "Couldn't remove unmapped copy {0}", orphan.Path);
                        continue;
                    }

                    orphan.Artist = artist;
                    _mediaFileService.Delete(orphan, DeleteMediaFileReason.Upgrade);
                }
            }
        }

        private static bool IsSameTrack(ParsedTrackInfo tags, Track track)
        {
            if (tags == null)
            {
                return false;
            }

            if (tags.RecordingMBId.IsNotNullOrWhiteSpace() && track.ForeignRecordingId.IsNotNullOrWhiteSpace())
            {
                return tags.RecordingMBId == track.ForeignRecordingId;
            }

            return (tags.TrackNumbers ?? Array.Empty<int>()).Contains(track.AbsoluteTrackNumber) &&
                   (tags.DiscNumber == 0 || tags.DiscNumber == track.MediumNumber) &&
                   tags.Title.IsNotNullOrWhiteSpace() &&
                   track.Title.IsNotNullOrWhiteSpace() &&
                   Parser.Parser.NormalizeTitle(tags.Title.CleanTrackTitle()) == Parser.Parser.NormalizeTitle(track.Title.CleanTrackTitle());
        }

        private void RemoveExistingTrackFiles(Artist artist, Album album)
        {
            var rootFolder = _diskProvider.GetParentFolder(artist.Path);
            var previousFiles = _mediaFileService.GetFilesByAlbum(album.Id);

            _logger.Debug($"Deleting {previousFiles.Count} existing files for {album}");

            foreach (var previousFile in previousFiles)
            {
                var subfolder = rootFolder.GetRelativePath(_diskProvider.GetParentFolder(previousFile.Path));
                if (_diskProvider.FileExists(previousFile.Path))
                {
                    _logger.Debug("Removing existing track file: {0}", previousFile);
                    _recycleBinProvider.DeleteFile(previousFile.Path, subfolder);
                }

                _mediaFileService.Delete(previousFile, DeleteMediaFileReason.Upgrade);
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

        // Moved aside before RemoveExistingTrackFiles, so a failed import can put the album back; that step then finds the files gone
        // and deletes only their rows, and their extras' rows, recorded as upgrades
        private ReplacedAlbum BackUpExistingTrackFiles(Artist artist, Album album)
        {
            var replacement = new ReplacedAlbum { Artist = artist, Album = album, RootFolder = _diskProvider.GetParentFolder(artist.Path), Started = DateTime.UtcNow };

            var previousFiles = _mediaFileService.GetFilesByAlbum(album.Id);
            var tracks = _trackService.GetTracksByFileId(previousFiles.Select(f => f.Id));

            try
            {
                foreach (var previousFile in previousFiles.Where(f => _diskProvider.FileExists(f.Path)))
                {
                    var fileTracks = tracks.Where(t => t.TrackFileId == previousFile.Id).ToList();
                    var file = new ReplacedFile { TrackFile = previousFile, TrackIds = fileTracks.Select(t => t.Id).ToList() };
                    replacement.ReleaseIds.AddRange(fileTracks.Select(t => t.AlbumReleaseId));

                    file.BackupPath = MoveAside(replacement, previousFile.Path);
                    replacement.Files.Add(file);

                    // Their rows go with the track file's; kept here so a put-back album gets them again
                    BackUpExtras(_lyricFileService, replacement, previousFile);
                    BackUpExtras(_metadataFileService, replacement, previousFile);
                    BackUpExtras(_otherExtraFileService, replacement, previousFile);
                }
            }
            catch
            {
                MoveBack(replacement);
                throw;
            }

            return replacement;
        }

        private void BackUpExtras<TExtraFile>(IExtraFileService<TExtraFile> service, ReplacedAlbum replacement, TrackFile trackFile)
            where TExtraFile : ExtraFile, new()
        {
            if (service == null)
            {
                return;
            }

            foreach (var row in service.GetFilesByTrackFile(trackFile.Id))
            {
                var path = Path.Combine(replacement.Artist.Path, row.RelativePath);

                if (_diskProvider.FileExists(path))
                {
                    replacement.Extras.Add(new ReplacedExtra
                    {
                        Row = row,
                        TrackFileId = trackFile.Id,
                        OriginalPath = path,
                        BackupPath = MoveAside(replacement, path),
                        Upsert = r => service.Upsert((TExtraFile)r)
                    });
                }
            }
        }

        // Each album gets its own folder, so one album's outcome never touches another's files
        private string MoveAside(ReplacedAlbum replacement, string path)
        {
            var folder = BackupFolder(_diskProvider.GetParentFolder(path), replacement.Album);
            var backupPath = FreePath(folder, Path.GetFileName(path));

            _diskProvider.CreateFolder(folder);
            _diskProvider.MoveFile(path, backupPath);

            return backupPath;
        }

        private static string BackupFolder(string folder, Album album)
        {
            return Path.Combine(folder, BackupFolderName, album.Id.ToString());
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

        private void FinishAlbum(Album album, AlbumRelease release, ReplacedAlbum replacement, List<ImportResult> importResults, List<TrackFile> importedFiles)
        {
            if (!importResults.Any(r => r.Result == ImportResultType.Imported && r.ImportDecision.Item.Album?.Id == album.Id))
            {
                if (replacement?.Files.Any() == true)
                {
                    _logger.Warn("No track of {0} imported, restoring its previous files", album);
                    TryRestore(replacement);
                }

                return;
            }

            _logger.Debug("Updating release to {0} [{1} tracks]", release, release.TrackCount);

            try
            {
                // The import loop cached the old list for the TrackImported events, so it gets the switch too, once it holds
                var cached = album.AlbumReleases.Value;
                album.AlbumReleases = _releaseService.SetMonitored(release);
                cached.ForEach(r => r.Monitored = r.Id == release.Id);
            }
            catch (Exception e)
            {
                // The new files are in; the previous ones stay where they wait rather than go to the bin
                _logger.Error(e, "Couldn't switch {0} to release {1}; its previous files are in {2}", album, release, replacement == null ? "-" : string.Join(", ", replacement.BackupFolders(_diskProvider)));
                return;
            }

            // Publish album edited event.
            // Deliberately don't put in the old album since we don't want to trigger an ArtistScan.
            _eventAggregator.PublishEvent(new AlbumEditedEvent(album, album));

            if (replacement == null)
            {
                return;
            }

            try
            {
                RecycleBackups(replacement, importedFiles.Where(f => f.AlbumId == album.Id).Select(f => BackupFolder(_diskProvider.GetParentFolder(f.Path), album)));
            }
            catch (Exception e)
            {
                // Logged, not thrown: the album has imported
                _logger.Error(e, "Couldn't recycle the replaced files of {0}; they are in {1}", album, string.Join(", ", replacement.BackupFolders(_diskProvider)));
            }
        }

        private void TryRestore(ReplacedAlbum replacement)
        {
            try
            {
                Restore(replacement);
            }
            catch (Exception e)
            {
                _logger.Error(e, "Couldn't finish restoring the previous files of {0}; any left aside are in {1}", replacement.Album, string.Join(", ", replacement.BackupFolders(_diskProvider)));
            }
        }

        // Everything in the album's own folders goes, including what an interrupted import of it left where the new files went
        private void RecycleBackups(ReplacedAlbum replacement, IEnumerable<string> importFolders)
        {
            foreach (var backupFolder in replacement.BackupFolders(_diskProvider).Concat(importFolders).Distinct().Where(_diskProvider.FolderExists))
            {
                var subfolder = replacement.RootFolder.GetRelativePath(_diskProvider.GetParentFolder(_diskProvider.GetParentFolder(backupFolder)));

                foreach (var file in _diskProvider.GetFiles(backupFolder, false))
                {
                    _logger.Debug("Removing existing track file: {0}", file);
                    _recycleBinProvider.DeleteFile(file, subfolder);
                }

                DeleteIfEmpty(backupFolder);
            }
        }

        private void Restore(ReplacedAlbum replacement)
        {
            MoveBack(replacement);

            var back = replacement.Files.Where(f => f.Restored).ToList();

            // RemoveExistingTrackFiles may have stopped partway; a row it left still has its path
            var gone = back.Where(f => _mediaFileService.GetFileWithPath(f.TrackFile.Path) == null).ToList();

            if (gone.Any())
            {
                var rows = gone.Select(f => CopyWithoutId(f.TrackFile)).ToList();
                _mediaFileService.AddMany(rows);

                // GetTracks(ids) would throw if one has gone since
                var tracks = _trackService.GetTracksByReleases(replacement.ReleaseIds.Distinct().ToList());

                for (var i = 0; i < gone.Count; i++)
                {
                    tracks.Where(t => gone[i].TrackIds.Contains(t.Id)).ToList().ForEach(t => t.TrackFileId = rows[i].Id);
                }

                _trackService.SetFileIds(tracks.Where(t => gone.Any(f => f.TrackIds.Contains(t.Id))).ToList());

                for (var i = 0; i < gone.Count; i++)
                {
                    foreach (var extra in replacement.Extras.Where(e => e.TrackFileId == gone[i].TrackFile.Id))
                    {
                        extra.Row.Id = 0;
                        extra.Row.TrackFileId = rows[i].Id;
                        extra.Upsert(extra.Row);
                    }
                }
            }

            try
            {
                // RemoveExistingTrackFiles recorded these as deleted
                _historyService.RemoveTrackFileDeleted(replacement.Album.Id, back.SelectMany(f => f.TrackIds, (f, id) => (id, f.TrackFile.Path)).ToDictionary(x => x.id, x => x.Path), replacement.Started);
            }
            catch (Exception e)
            {
                _logger.Warn(e, "Couldn't remove the deletion history of the files put back for {0}", replacement.Album);
            }
        }

        // One file that can't move leaves the rest to go back, and waits where the error says
        private void MoveBack(ReplacedAlbum replacement)
        {
            foreach (var file in replacement.Files.Where(f => _diskProvider.FileExists(f.BackupPath)))
            {
                try
                {
                    // Another album's import holds this path; it keeps its file, and this one goes to the bin as develop's upgrade sent it
                    var held = HeldByAnotherImport(replacement, file.TrackFile.Path);
                    PutBack(replacement, file.BackupPath, file.TrackFile.Path, held);
                    file.Recycled = held;
                    file.Restored = !held;
                }
                catch (Exception e)
                {
                    _logger.Error(e, "Couldn't move the previous file {0} out of {1}", file.TrackFile.Path, _diskProvider.GetParentFolder(file.BackupPath));
                }
            }

            // An extra follows its track file, and waits with one that couldn't move
            foreach (var extra in replacement.Extras.Where(e => _diskProvider.FileExists(e.BackupPath)))
            {
                var file = replacement.Files.Single(f => f.TrackFile.Id == extra.TrackFileId);

                if (!file.Recycled && !file.Restored)
                {
                    continue;
                }

                try
                {
                    PutBack(replacement, extra.BackupPath, extra.OriginalPath, file.Recycled);
                }
                catch (Exception e)
                {
                    _logger.Error(e, "Couldn't move the previous file {0} out of {1}", extra.OriginalPath, _diskProvider.GetParentFolder(extra.BackupPath));
                }
            }

            try
            {
                replacement.BackupFolders(_diskProvider).ForEach(DeleteIfEmpty);
            }
            catch (Exception e)
            {
                _logger.Warn(e, "Couldn't remove the emptied folders {0}", string.Join(", ", replacement.BackupFolders(_diskProvider)));
            }
        }

        private void PutBack(ReplacedAlbum replacement, string backupPath, string original, bool recycle)
        {
            var subfolder = replacement.RootFolder.GetRelativePath(_diskProvider.GetParentFolder(original));

            if (recycle)
            {
                _recycleBinProvider.DeleteFile(backupPath, subfolder);
                return;
            }

            // This album's import failed, so a file on the old path is one of its new files
            if (_diskProvider.FileExists(original))
            {
                _recycleBinProvider.DeleteFile(original, subfolder);
            }

            _diskProvider.MoveFile(backupPath, original);
        }

        // A case- or normalisation-insensitive file system resolves the old path to a file another import named differently
        private bool HeldByAnotherImport(ReplacedAlbum replacement, string path)
        {
            var row = _mediaFileService.GetFileWithPath(path);

            if (row == null && _diskProvider.FileExists(path))
            {
                var onDisk = _diskProvider.GetFiles(_diskProvider.GetParentFolder(path), false);
                var resolved = onDisk.Contains(path) ? null : onDisk.FirstOrDefault(f => string.Equals(f.Normalize(), path.Normalize(), StringComparison.OrdinalIgnoreCase));
                row = resolved == null ? null : _mediaFileService.GetFileWithPath(resolved);
            }

            return row != null && replacement.Files.All(f => f.TrackFile.Id != row.Id);
        }

        private void DeleteUnlinkedRows(List<TrackFile> files)
        {
            try
            {
                _mediaFileService.DeleteMany(files.Where(f => f.Id > 0).ToList(), DeleteMediaFileReason.ManualOverride);
            }
            catch (Exception e)
            {
                _logger.Warn(e, "Couldn't remove the rows of the new files that didn't link");
            }
        }

        // The album's folder, then the shared hidden folder above it once no album is using it
        private void DeleteIfEmpty(string folder)
        {
            foreach (var candidate in new[] { folder, _diskProvider.GetParentFolder(folder) })
            {
                if (_diskProvider.FolderExists(candidate) && _diskProvider.FolderEmpty(candidate))
                {
                    _diskProvider.DeleteFolder(candidate, false);
                }
            }
        }

        private static TrackFile CopyWithoutId(TrackFile file)
        {
            return new TrackFile
            {
                AlbumId = file.AlbumId,
                Path = file.Path,
                Size = file.Size,
                Modified = file.Modified,
                DateAdded = file.DateAdded,
                OriginalFilePath = file.OriginalFilePath,
                SceneName = file.SceneName,
                ReleaseGroup = file.ReleaseGroup,
                Quality = file.Quality,
                IndexerFlags = file.IndexerFlags,
                MediaInfo = file.MediaInfo
            };
        }

        private class ReplacedAlbum
        {
            public Artist Artist { get; set; }
            public Album Album { get; set; }
            public string RootFolder { get; set; }
            public DateTime Started { get; set; }
            public List<int> ReleaseIds { get; set; } = new List<int>();
            public List<ReplacedFile> Files { get; } = new List<ReplacedFile>();
            public List<ReplacedExtra> Extras { get; } = new List<ReplacedExtra>();

            public List<string> BackupFolders(IDiskProvider diskProvider)
            {
                return Files.Select(f => f.BackupPath).Concat(Extras.Select(e => e.BackupPath)).Where(p => p != null).Select(diskProvider.GetParentFolder).Distinct().ToList();
            }
        }

        private class ReplacedFile
        {
            public TrackFile TrackFile { get; set; }
            public string BackupPath { get; set; }
            public List<int> TrackIds { get; set; }
            public bool Recycled { get; set; }
            public bool Restored { get; set; }
        }

        private class ReplacedExtra
        {
            public ExtraFile Row { get; set; }
            public int TrackFileId { get; set; }
            public string OriginalPath { get; set; }
            public string BackupPath { get; set; }
            public Action<ExtraFile> Upsert { get; set; }
        }
    }
}
