using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Extras.Files;
using NzbDrone.Core.Extras.Lyrics;
using NzbDrone.Core.Extras.Metadata.Files;
using NzbDrone.Core.Extras.Others;
using NzbDrone.Core.History;
using NzbDrone.Core.Music;

namespace NzbDrone.Core.MediaFiles.TrackImport
{
    public interface IReplaceTrackFiles
    {
        ReplacedAlbum BackUp(Artist artist, Album album);
        void Restore(ReplacedAlbum replacement);
        void Recycle(ReplacedAlbum replacement, IEnumerable<TrackFile> importedFiles);
        TrackFile PutBack(TrackFile deletedFile);
    }

    // Moves an album's files aside before an import replaces them, then puts them back or recycles them once the import's outcome is known
    public class TrackFileReplacementService : IReplaceTrackFiles
    {
        // Hidden, so a disk scan skips the files waiting here (DiskScanService.ExcludedSubFoldersRegex)
        private const string BackupFolderName = ".lidarr-replaced";

        private readonly IMediaFileService _mediaFileService;
        private readonly ITrackService _trackService;
        private readonly IDiskProvider _diskProvider;
        private readonly IRecycleBinProvider _recycleBinProvider;
        private readonly IHistoryRepository _historyRepository;
        private readonly ILyricFileService _lyricFileService;
        private readonly IMetadataFileService _metadataFileService;
        private readonly IOtherExtraFileService _otherExtraFileService;
        private readonly Logger _logger;

        public TrackFileReplacementService(IMediaFileService mediaFileService,
                                           ITrackService trackService,
                                           IDiskProvider diskProvider,
                                           IRecycleBinProvider recycleBinProvider,
                                           IHistoryRepository historyRepository,
                                           ILyricFileService lyricFileService,
                                           IMetadataFileService metadataFileService,
                                           IOtherExtraFileService otherExtraFileService,
                                           Logger logger)
        {
            _mediaFileService = mediaFileService;
            _trackService = trackService;
            _diskProvider = diskProvider;
            _recycleBinProvider = recycleBinProvider;
            _historyRepository = historyRepository;
            _lyricFileService = lyricFileService;
            _metadataFileService = metadataFileService;
            _otherExtraFileService = otherExtraFileService;
            _logger = logger;
        }

        // Before RemoveExistingTrackFiles, which then finds the files gone and deletes only their rows, and their extras' rows, recorded as upgrades
        public ReplacedAlbum BackUp(Artist artist, Album album)
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

        public void Restore(ReplacedAlbum replacement)
        {
            try
            {
                PutAlbumBack(replacement);
            }
            catch (Exception e)
            {
                _logger.Error(e, "Couldn't finish restoring the previous files of {0}; any left aside are in {1}", replacement.Album, string.Join(", ", replacement.BackupFolders(_diskProvider)));
            }
        }

        // Everything in the album's own folders goes, including what an interrupted import of it left where the new files went
        public void Recycle(ReplacedAlbum replacement, IEnumerable<TrackFile> importedFiles)
        {
            var importFolders = importedFiles.Where(f => f.AlbumId == replacement.Album.Id).Select(f => BackupFolder(_diskProvider.GetParentFolder(f.Path), replacement.Album));

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

        // A file imported again at its own path loses its row before AddMany; a failed import gives it back, linked to the same tracks
        public TrackFile PutBack(TrackFile deletedFile)
        {
            var row = _mediaFileService.Add(CopyWithoutId(deletedFile));
            var tracks = deletedFile.Tracks.Value;

            tracks.ForEach(t => t.TrackFileId = row.Id);
            _trackService.SetFileIds(tracks);

            return row;
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

        private void PutAlbumBack(ReplacedAlbum replacement)
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

            if (_historyRepository == null || back.Empty())
            {
                return;
            }

            try
            {
                RemoveDeletionHistory(replacement, back);
            }
            catch (Exception e)
            {
                _logger.Warn(e, "Couldn't remove the deletion history of the files put back for {0}", replacement.Album);
            }
        }

        // RemoveExistingTrackFiles recorded these as deleted; stored keys are camelCase
        private void RemoveDeletionHistory(ReplacedAlbum replacement, List<ReplacedFile> back)
        {
            var pathsByTrack = back.SelectMany(f => f.TrackIds, (f, id) => (id, f.TrackFile.Path)).ToDictionary(x => x.id, x => x.Path);
            var entries = _historyRepository.GetByAlbum(replacement.Album.Id, EntityHistoryEventType.TrackFileDeleted)
                .Where(h => h.Date >= replacement.Started && pathsByTrack.TryGetValue(h.TrackId, out var path) && h.SourceTitle == path && h.Data.GetValueOrDefault("reason") == nameof(DeleteMediaFileReason.Upgrade))
                .Select(h => h.Id)
                .ToList();

            _historyRepository.DeleteMany(entries);
        }

        // One file that can't move leaves the rest to go back, and waits where the error says
        private void MoveBack(ReplacedAlbum replacement)
        {
            foreach (var file in replacement.Files.Where(f => _diskProvider.FileExists(f.BackupPath)))
            {
                try
                {
                    // Another album's import holds this path; it keeps its file, and this one goes to the bin as develop's upgrade sent it
                    var held = HeldByAnotherAlbum(replacement, file.TrackFile.Path);
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

            // No other album holds the old path, so a file on it is one of this album's new files
            if (_diskProvider.FileExists(original))
            {
                _recycleBinProvider.DeleteFile(original, subfolder);
            }

            _diskProvider.MoveFile(backupPath, original);
        }

        // Exact, as Lidarr compares paths on Linux; a row of this album that isn't one of its old files is one of its new ones
        private bool HeldByAnotherAlbum(ReplacedAlbum replacement, string path)
        {
            var row = _mediaFileService.GetFileWithPath(path);

            return row != null && row.AlbumId != replacement.Album.Id;
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
    }
}
