using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Music;
using NzbDrone.Core.Music.Events;

namespace NzbDrone.Core.Extras.Files
{
    public interface IExtraFileService<TExtraFile>
        where TExtraFile : ExtraFile, new()
    {
        List<TExtraFile> GetFilesByArtist(int artistId);
        List<TExtraFile> GetFilesByTrackFile(int trackFileId);
        TExtraFile FindByPath(int artistId, string path);
        void Upsert(TExtraFile extraFile);
        void Upsert(List<TExtraFile> extraFiles);
        void Delete(int id);
        void DeleteMany(IEnumerable<int> ids);
    }

    public interface IRelinkExtraFiles
    {
        void Relink(int fromTrackFileId, TrackFile toTrackFile);
    }

    public abstract class ExtraFileService<TExtraFile> : IExtraFileService<TExtraFile>,
                                                         IRelinkExtraFiles,
                                                         IHandleAsync<ArtistsDeletedEvent>,
                                                         IHandle<AlbumDeletedEvent>,
                                                         IHandle<TrackFileDeletedEvent>
        where TExtraFile : ExtraFile, new()
    {
        private readonly IExtraFileRepository<TExtraFile> _repository;
        private readonly IArtistService _artistService;
        private readonly IMediaFileService _mediaFileService;
        private readonly IDiskProvider _diskProvider;
        private readonly IRecycleBinProvider _recycleBinProvider;
        private readonly Logger _logger;

        // Develop's constructor, kept for plugins built against it; an album deleted with its files then leaves its unlinked extras on disk
        public ExtraFileService(IExtraFileRepository<TExtraFile> repository,
                                IArtistService artistService,
                                IDiskProvider diskProvider,
                                IRecycleBinProvider recycleBinProvider,
                                Logger logger)
            : this(repository, artistService, null, diskProvider, recycleBinProvider, logger)
        {
        }

        public ExtraFileService(IExtraFileRepository<TExtraFile> repository,
                                IArtistService artistService,
                                IMediaFileService mediaFileService,
                                IDiskProvider diskProvider,
                                IRecycleBinProvider recycleBinProvider,
                                Logger logger)
        {
            _repository = repository;
            _artistService = artistService;
            _mediaFileService = mediaFileService;
            _diskProvider = diskProvider;
            _recycleBinProvider = recycleBinProvider;
            _logger = logger;
        }

        public List<TExtraFile> GetFilesByArtist(int artistId)
        {
            return _repository.GetFilesByArtist(artistId);
        }

        public List<TExtraFile> GetFilesByTrackFile(int trackFileId)
        {
            return _repository.GetFilesByTrackFile(trackFileId);
        }

        public TExtraFile FindByPath(int artistId, string path)
        {
            return _repository.FindByPath(artistId, path);
        }

        public void Upsert(TExtraFile extraFile)
        {
            Upsert(new List<TExtraFile> { extraFile });
        }

        public void Upsert(List<TExtraFile> extraFiles)
        {
            extraFiles.ForEach(m =>
            {
                m.LastUpdated = DateTime.UtcNow;

                if (m.Id == 0)
                {
                    m.Added = m.LastUpdated;
                }
            });

            _repository.InsertMany(extraFiles.Where(m => m.Id == 0).ToList());
            _repository.UpdateMany(extraFiles.Where(m => m.Id > 0).ToList());
        }

        public void Delete(int id)
        {
            _repository.Delete(id);
        }

        public void DeleteMany(IEnumerable<int> ids)
        {
            _repository.DeleteMany(ids);
        }

        public void HandleAsync(ArtistsDeletedEvent message)
        {
            _repository.DeleteForArtists(message.Artists.Select(x => x.Id).ToList());
        }

        // Its track files go to the recycle bin with no events of their own, so their extras go here; synchronous, so before the
        // asynchronous MediaFileDeletionService looks for the album folder to be empty
        public void Handle(AlbumDeletedEvent message)
        {
            if (!message.DeleteFiles)
            {
                return;
            }

            // Only the extras of the files being deleted, and the album's own: a file on an unmonitored release stays, with its extras
            var artist = message.Album.Artist.Value;
            var deletedFiles = message.TrackFilesToDelete ?? new List<TrackFile>();
            var deletedTrackFileIds = deletedFiles.Select(f => f.Id).ToHashSet();
            var albumExtras = _repository.GetFilesByAlbum(artist.Id, message.Album.Id);

            // A row stored unlinked goes with the album, and its file too when the one track file it's named after is being deleted;
            // MediaFileService drops the album's rows only after this, so the files it keeps are still there to tie with
            var unlinked = albumExtras.Where(e => e.TrackFileId == 0).ToList();
            var artistFiles = unlinked.Any() && _mediaFileService != null ? _mediaFileService.GetFilesWithBasePath(artist.Path) : new List<TrackFile>();
            var extraFiles = albumExtras.Where(e => e.TrackFileId == null || deletedTrackFileIds.Contains(e.TrackFileId.Value) ||
                                                    (e.TrackFileId == 0 && deletedTrackFileIds.Contains(ExtraFileNames.FindTrackFile(Path.Combine(artist.Path, e.RelativePath), artistFiles)?.Id ?? 0)))
                                        .ToList();

            Recycle(artist, extraFiles);
            _repository.DeleteMany(extraFiles.Union(unlinked).ToList());
        }

        // The file may now belong to another album, so the album moves with the track file
        public void Relink(int fromTrackFileId, TrackFile toTrackFile)
        {
            var extraFiles = _repository.GetFilesByTrackFile(fromTrackFileId);

            extraFiles.ForEach(e =>
            {
                e.TrackFileId = toTrackFile.Id;
                e.AlbumId = toTrackFile.AlbumId;
            });
            _repository.SetFields(extraFiles, e => e.TrackFileId, e => e.AlbumId);
        }

        public void Handle(TrackFileDeletedEvent message)
        {
            var trackFile = message.TrackFile;

            // The same file imported again under a new row: ImportApprovedTracks relinks its extras to that row
            if (message.Reason == DeleteMediaFileReason.ManualOverride)
            {
                return;
            }

            // A file gone from disk may only have been renamed outside Lidarr: what came with it stays beside it
            if (message.Reason is DeleteMediaFileReason.NoLinkedEpisodes or DeleteMediaFileReason.MissingFromDisk)
            {
                _logger.Debug("Removing track file from DB ({0}), not deleting extra files from disk.", message.Reason);
            }
            else
            {
                Recycle(trackFile.Artist.Value, _repository.GetFilesByTrackFile(trackFile.Id));
            }

            _logger.Debug("Deleting Extra from database for track file: {0}", trackFile);
            _repository.DeleteForTrackFile(trackFile.Id);
        }

        // Each file on its own, so one that can't be recycled leaves the others to go; returns those no longer on disk
        protected List<TExtraFile> Recycle(Artist artist, IEnumerable<TExtraFile> extraFiles)
        {
            var gone = new List<TExtraFile>();

            foreach (var extra in extraFiles)
            {
                var path = Path.Combine(artist.Path, extra.RelativePath);

                try
                {
                    if (_diskProvider.FileExists(path))
                    {
                        // Send to the recycling bin so they can be recovered if necessary
                        var subfolder = _diskProvider.GetParentFolder(artist.Path).GetRelativePath(_diskProvider.GetParentFolder(path));
                        _recycleBinProvider.DeleteFile(path, subfolder);
                    }

                    gone.Add(extra);
                }
                catch (Exception e)
                {
                    _logger.Warn(e, "Couldn't recycle extra file {0}, it stays on disk", path);
                }
            }

            return gone;
        }
    }
}
